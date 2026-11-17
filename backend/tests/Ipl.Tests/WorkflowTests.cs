using System.Net;
using System.Text.Json;
using Ipl.Domain;
using Microsoft.AspNetCore.SignalR.Client;

namespace Ipl.Tests;

[Collection("api")]
public class AuthAndSecurityTests(ApiFactory api)
{
    [Fact]
    public async Task Login_returns_token_and_profile_and_wrong_password_is_rejected()
    {
        var c = await api.LoginAsync("owner.mum@demo.local");
        var me = await c.OkAsync(c.GetAsync("auth/me"));
        Assert.Equal("owner.mum@demo.local", me.GetProperty("email").GetString());
        Assert.Contains(me.GetProperty("permissions").EnumerateArray(), p => p.GetString() == Perm.TradesApprove);

        var anon = new ApiClient(api.CreateClient());
        var bad = await anon.PostAsync("auth/login", new { email = "owner.mum@demo.local", password = "wrong-Password1" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.Status);
        Assert.Equal("Incorrect e-mail or password.", bad.Error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures()
    {
        var anon = new ApiClient(api.CreateClient());
        for (var i = 0; i < 5; i++) await anon.PostAsync("auth/login", new { email = "assistant.coach.mum@demo.local", password = "Wrong-password-1" });
        var locked = await anon.PostAsync("auth/login", new { email = "assistant.coach.mum@demo.local", password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.Status);
        Assert.Contains("Too many failed attempts", locked.Raw);
    }

    [Fact]
    public async Task Refresh_rotates_and_reusing_an_old_token_ends_every_session()
    {
        var c = await api.LoginAsync("analyst.mum@demo.local");
        var first = c.RefreshCookie!;
        var r1 = await c.SendAsync(HttpMethod.Post, "auth/refresh", new { }, first);
        Assert.Equal(HttpStatusCode.OK, r1.Status);
        var second = r1.SetCookie!;
        Assert.NotEqual(first, second);
        // The first token was rotated: using it again looks like theft and revokes the family (including "second").
        var reuse = await c.SendAsync(HttpMethod.Post, "auth/refresh", new { }, first);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.Status);
        var after = await c.SendAsync(HttpMethod.Post, "auth/refresh", new { }, second);
        Assert.Equal(HttpStatusCode.Unauthorized, after.Status);
    }

    [Fact]
    public async Task Requests_without_a_token_are_rejected()
    {
        var anon = new ApiClient(api.CreateClient());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("players")).Status);
    }

    [Fact]
    public async Task Permissions_and_tenant_isolation_are_enforced_on_the_server()
    {
        var owner = await api.LoginAsync("owner.mum@demo.local");
        var medical = await api.LoginAsync("medical.staff.mum@demo.local");
        var chn = await api.LoginAsync("owner.chn@demo.local");
        var scout = await api.LoginAsync("scout.mum@demo.local");

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("medical/records")).Status);            // medical details: medical staff only
        Assert.Equal(HttpStatusCode.OK, (await medical.GetAsync("medical/records")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await scout.GetAsync("finance/overview")).Status);           // scouts have no finance access
        Assert.Equal(HttpStatusCode.Forbidden, (await scout.PostAsync("auctions/" + Guid.NewGuid() + "/bids", new { lotId = Guid.NewGuid(), amount = 1, idempotencyKey = "x" })).Status);

        var mumId = (await owner.OkAsync(owner.GetAsync("auth/me"))).GetProperty("activeFranchiseId").GetString();
        var other = await chn.GetAsync($"finance/overview?franchiseId={mumId}");
        Assert.Equal(HttpStatusCode.Forbidden, other.Status);                                                // other franchise's finances
        var reports = await chn.GetAsync("scouting/reports");
        Assert.Equal(HttpStatusCode.Forbidden, reports.Status);                                             // CHN owner has no scouting perm
        // Mumbai's internal note on the seeded trade must never reach Chennai.
        var trades = await chn.OkAsync(chn.GetAsync("trades"));
        var tradeId = trades.GetProperty("items")[0].GetProperty("id").GetString();
        var detail = await chn.OkAsync(chn.GetAsync($"trades/{tradeId}"));
        Assert.DoesNotContain(detail.GetProperty("negotiations").EnumerateArray(), n => n.GetProperty("isInternal").GetBoolean());
        var mine = await owner.OkAsync(owner.GetAsync($"trades/{tradeId}"));
        Assert.Contains(mine.GetProperty("negotiations").EnumerateArray(), n => n.GetProperty("isInternal").GetBoolean());
    }

    [Fact]
    public async Task Franchise_managers_cannot_grant_permissions_they_do_not_hold()
    {
        var owner = await api.LoginAsync("owner.mum@demo.local");
        var me = await owner.OkAsync(owner.GetAsync("auth/me"));
        var fid = me.GetProperty("activeFranchiseId").GetString();
        var medicalRole = await owner.PostAsync("users/invite", new { email = $"doc{Guid.NewGuid():N}@demo.local", fullName = "New Doctor", franchiseId = fid, jobTitle = "Doctor", roleCodes = new[] { RoleCatalog.Medical } });
        Assert.Equal(HttpStatusCode.Forbidden, medicalRole.Status);
        var analyst = await owner.OkAsync(owner.PostAsync("users/invite", new { email = $"an{Guid.NewGuid():N}@demo.local", fullName = "New Analyst", franchiseId = fid, jobTitle = "Analyst", roleCodes = new[] { RoleCatalog.Viewer } }));
        Assert.Contains("accept-invitation?token=", analyst.GetProperty("invitationLink").GetString());
    }
}

[Collection("api")]
public class TradeLifecycleTests(ApiFactory api)
{
    [Fact]
    public async Task Full_trade_from_offer_to_official_transfer()
    {
        var mumDirector = await api.LoginAsync("cricket.director.mum@demo.local");
        var mumOwner = await api.LoginAsync("owner.mum@demo.local");
        var mumOps = await api.LoginAsync("team.operations.mum@demo.local");
        var chnDirector = await api.LoginAsync("cricket.director.chn@demo.local");
        var chnOwner = await api.LoginAsync("owner.chn@demo.local");
        var chnOps = await api.LoginAsync("team.operations.chn@demo.local");
        var league = await api.LoginAsync("league@demo.local");

        var mumSquad = await mumDirector.OkAsync(mumDirector.GetAsync("squad"));
        var chnId = (await chnOwner.OkAsync(chnOwner.GetAsync("auth/me"))).GetProperty("activeFranchiseId").GetString()!;
        var chnSquad = await chnOwner.OkAsync(chnOwner.GetAsync($"franchises/{chnId}/squad"));
        // Pick players not already in the seeded open trade.
        var seededTrade = await mumDirector.OkAsync(mumDirector.GetAsync("trades"));
        var busy = new HashSet<string>();
        foreach (var t in seededTrade.GetProperty("items").EnumerateArray())
            foreach (var p in (await mumDirector.OkAsync(mumDirector.GetAsync($"trades/{t.GetProperty("id").GetString()}"))).GetProperty("players").EnumerateArray())
                busy.Add(p.GetProperty("playerId").GetString()!);
        var outgoing = mumSquad.GetProperty("players").EnumerateArray().First(p => p.GetProperty("roleCode").GetString() == "allrounder" && !p.GetProperty("isOverseas").GetBoolean() && !busy.Contains(p.GetProperty("playerId").GetString()!));
        var incoming = chnSquad.GetProperty("players").EnumerateArray().First(p => p.GetProperty("roleCode").GetString() == "bowler" && !p.GetProperty("isOverseas").GetBoolean() && !busy.Contains(p.GetProperty("playerId").GetString()!));
        var outId = outgoing.GetProperty("playerId").GetString()!;
        var inId = incoming.GetProperty("playerId").GetString()!;
        var mumId = mumSquad.GetProperty("franchiseId").GetString()!;
        var purseBefore = mumSquad.GetProperty("purseRemaining").GetDecimal();

        // Invalid: offering a player from the other squad.
        var invalid = await mumDirector.PostAsync("trades", new { terms = new { counterpartyFranchiseId = chnId, playersOffered = new[] { inId }, playersRequested = Array.Empty<string>(), cashConsideration = 0, cashPayerFranchiseId = (string?)null, conditions = (string?)null }, submit = false });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);

        // Impact preview separates salary, consideration and purse.
        var impact = await mumDirector.OkAsync(mumDirector.PostAsync("trades/impact", new { counterpartyFranchiseId = chnId, playersOffered = new[] { outId }, playersRequested = new[] { inId }, cashConsideration = 2_500_000, cashPayerFranchiseId = mumId }));
        Assert.Equal(2, impact.GetProperty("ruleNotes").GetArrayLength());
        Assert.True(impact.GetProperty("isValid").GetBoolean());

        var created = await mumDirector.OkAsync(mumDirector.PostAsync("trades", new
        {
            terms = new { counterpartyFranchiseId = chnId, playersOffered = new[] { outId }, playersRequested = new[] { inId }, cashConsideration = 2_500_000, cashPayerFranchiseId = mumId, conditions = "Medical clearance" },
            submit = true, message = "Opening offer",
        }));
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal("PendingResponse", created.GetProperty("status").GetString());
        Assert.Equal("PlayersPlusCash", created.GetProperty("type").GetString());

        // MUM can't accept its own terms; CHN counters (asks for more cash); MUM accepts.
        Assert.Equal(HttpStatusCode.Conflict, (await mumDirector.PostAsync($"trades/{id}/accept", new { })).Status);
        var countered = await chnDirector.OkAsync(chnDirector.PostAsync($"trades/{id}/counter", new { playersOffered = new[] { inId }, playersRequested = new[] { outId }, cashConsideration = 5_000_000, cashPayerFranchiseId = mumId, conditions = "Medical clearance", message = "Need ₹50 L" }));
        Assert.Equal("CounterofferReceived", countered.GetProperty("status").GetString());
        Assert.Equal(2, countered.GetProperty("revisionNo").GetInt32());
        var accepted = await mumDirector.OkAsync(mumDirector.PostAsync($"trades/{id}/accept", new { message = "Agreed" }));
        Assert.Equal("AcceptedInPrinciple", accepted.GetProperty("status").GetString());

        // Approvals: franchise authorisations → player consent → league.
        async Task Decide(ApiClient who, string type, string? franchise = null, bool approve = true)
        {
            var detail = await who.OkAsync(who.GetAsync($"trades/{id}"));
            var approval = detail.GetProperty("approvals").EnumerateArray().First(a => a.GetProperty("type").GetString() == type && a.GetProperty("decision").GetString() == "Pending"
                && (franchise is null || a.GetProperty("franchiseId").GetString() == franchise) && a.GetProperty("canDecide").GetBoolean());
            await who.OkAsync(who.PostAsync($"trades/{id}/approvals/{approval.GetProperty("id").GetString()}", new { approve, comment = approve ? null : "No" }));
        }
        // League cannot approve before the franchises and players.
        var early = await league.OkAsync(league.GetAsync($"trades/{id}"));
        var leagueApproval = early.GetProperty("approvals").EnumerateArray().First(a => a.GetProperty("type").GetString() == "LeagueApproval");
        Assert.Equal(HttpStatusCode.Conflict, (await league.PostAsync($"trades/{id}/approvals/{leagueApproval.GetProperty("id").GetString()}", new { approve = true })).Status);
        // Completion before all approvals is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await league.PostAsync($"trades/{id}/complete")).Status);

        await Decide(mumOwner, "FranchiseAuthorization", mumId);
        await Decide(chnOwner, "FranchiseAuthorization", chnId);
        await Decide(mumOps, "PlayerConsent", mumId);
        await Decide(chnOps, "PlayerConsent", chnId);
        var awaiting = await league.OkAsync(league.GetAsync($"trades/{id}"));
        Assert.Equal("AwaitingLeagueApproval", awaiting.GetProperty("status").GetString());
        await Decide(league, "LeagueApproval");

        var done = await league.OkAsync(league.PostAsync($"trades/{id}/complete"));
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await league.PostAsync($"trades/{id}/complete")).Status); // idempotent: never twice

        // Players moved, squads updated, consideration booked in its own ledger, purse changed by salaries only.
        var after = await mumOwner.OkAsync(mumOwner.GetAsync("squad"));
        var ids = after.GetProperty("players").EnumerateArray().Select(p => p.GetProperty("playerId").GetString()).ToList();
        Assert.Contains(inId, ids);
        Assert.DoesNotContain(outId, ids);
        var player = await mumOwner.OkAsync(mumOwner.GetAsync($"players/{inId}"));
        Assert.Equal(mumId, player.GetProperty("franchise").GetProperty("id").GetString());
        var ledger = await mumOwner.OkAsync(mumOwner.GetAsync("finance/transactions?category=TradeConsideration"));
        Assert.Contains(ledger.GetProperty("items").EnumerateArray(), t => t.GetProperty("amount").GetDecimal() == 5_000_000 && t.GetProperty("direction").GetString() == "Debit");
        var salaryIn = incoming.GetProperty("price").GetDecimal();
        var salaryOut = outgoing.GetProperty("price").GetDecimal();
        Assert.Equal(purseBefore + salaryOut - salaryIn, after.GetProperty("purseRemaining").GetDecimal());
    }

    [Fact]
    public async Task Trades_can_be_rejected_and_withdrawn()
    {
        var mum = await api.LoginAsync("ceo.mum@demo.local");
        var chn = await api.LoginAsync("ceo.chn@demo.local");
        var mumSquad = await mum.OkAsync(mum.GetAsync("squad"));
        var chnId = (await chn.OkAsync(chn.GetAsync("auth/me"))).GetProperty("activeFranchiseId").GetString()!;
        var pid = mumSquad.GetProperty("players").EnumerateArray().Last().GetProperty("playerId").GetString();
        var t = await mum.OkAsync(mum.PostAsync("trades", new { terms = new { counterpartyFranchiseId = chnId, playersOffered = new[] { pid }, playersRequested = Array.Empty<string>(), cashConsideration = 0 }, submit = true }));
        var id = t.GetProperty("id").GetString();
        var rejected = await chn.OkAsync(chn.PostAsync($"trades/{id}/reject", new { message = "Not interested" }));
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await mum.PostAsync($"trades/{id}/withdraw", new { })).Status);

        var draft = await mum.OkAsync(mum.PostAsync("trades", new { terms = new { counterpartyFranchiseId = chnId, playersOffered = new[] { pid }, playersRequested = Array.Empty<string>(), cashConsideration = 0 }, submit = false }));
        var draftId = draft.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await chn.GetAsync($"trades/{draftId}")).Status); // drafts are private
        var withdrawn = await mum.OkAsync(mum.PostAsync($"trades/{draftId}/withdraw", new { }));
        Assert.Equal("Withdrawn", withdrawn.GetProperty("status").GetString());
    }
}

[Collection("api")]
public class AuctionTests(ApiFactory api)
{
    [Fact]
    public async Task Live_bidding_validates_rules_handles_races_and_books_the_sale()
    {
        var league = await api.LoginAsync("league@demo.local");
        var blr = await api.LoginAsync("auction.blr@demo.local");
        var kol = await api.LoginAsync("auction.kol@demo.local");
        var del = await api.LoginAsync("auction.del@demo.local");
        var viewer = await api.LoginAsync("viewer.mum@demo.local");

        // A fresh auction with two players from the free-agent pool (the seeded auction stays untouched).
        var pool = await league.OkAsync(league.GetAsync("players?status=AvailableForAuction&pageSize=50"));
        var seeded = (await league.OkAsync(league.GetAsync("auctions")))[0].GetProperty("id").GetString();
        var ids = pool.GetProperty("items").EnumerateArray().Where(p => !p.GetProperty("isOverseas").GetBoolean()).Select(p => p.GetProperty("id").GetString()).Take(2).ToArray();
        var auctionId = (await league.OkAsync(league.PostAsync("auctions", new { name = "Test auction", isMegaAuction = false, bidTimerSeconds = 30, allowReopenUnsold = true }))).GetProperty("id").GetString();
        var added = await league.OkAsync(league.PostAsync($"auctions/{auctionId}/lots", new { playerIds = ids, setName = "Test set", setNumber = 1, basePrice = 2_000_000 }));
        Assert.Equal(2, added.GetProperty("added").GetInt32());

        // SignalR: a watcher in the auction room receives the bid event.
        var events = new List<string>();
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Server.BaseAddress, "/hubs/live"), o => { o.HttpMessageHandlerFactory = _ => api.Server.CreateHandler(); o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling; o.AccessTokenProvider = () => Task.FromResult(viewer.Token); })
            .Build();
        hub.On<JsonElement>("auction", e => { lock (events) events.Add(e.GetProperty("kind").GetString()!); });
        await hub.StartAsync();
        await hub.InvokeAsync("JoinAuction", Guid.Parse(auctionId!));

        Assert.Equal(HttpStatusCode.Forbidden, (await blr.PostAsync($"auctions/{auctionId}/start")).Status); // franchises can't run auctions
        await league.OkAsync(league.PostAsync($"auctions/{auctionId}/start"));
        var live = await league.OkAsync(league.PostAsync($"auctions/{auctionId}/open-lot", new { lotId = (string?)null }));
        var lotId = live.GetProperty("currentLot").GetProperty("id").GetString();
        Assert.Equal(2_000_000, live.GetProperty("nextBid").GetDecimal());

        // Wrong amount is refused; first valid bid is the base price.
        var wrong = await blr.PostAsync($"auctions/{auctionId}/bids", new { lotId, amount = 9_000_000, idempotencyKey = Guid.NewGuid().ToString("N") });
        Assert.Equal((HttpStatusCode)422, wrong.Status);
        var key = Guid.NewGuid().ToString("N");
        await blr.OkAsync(blr.PostAsync($"auctions/{auctionId}/bids", new { lotId, amount = 2_000_000, idempotencyKey = key }));
        // A retried request (same key) does not place a second bid.
        await blr.OkAsync(blr.PostAsync($"auctions/{auctionId}/bids", new { lotId, amount = 2_000_000, idempotencyKey = key }));
        // The highest bidder cannot raise their own bid.
        Assert.Equal((HttpStatusCode)422, (await blr.PostAsync($"auctions/{auctionId}/bids", new { lotId, amount = 2_500_000, idempotencyKey = Guid.NewGuid().ToString("N") })).Status);

        // Two franchises race for the same next increment: exactly one wins.
        var race = await Task.WhenAll(
            kol.PostAsync($"auctions/{auctionId}/bids", new { lotId, amount = 2_500_000, idempotencyKey = Guid.NewGuid().ToString("N") }),
            del.PostAsync($"auctions/{auctionId}/bids", new { lotId, amount = 2_500_000, idempotencyKey = Guid.NewGuid().ToString("N") }));
        Assert.Equal(1, race.Count(r => r.Status == HttpStatusCode.OK));
        var state = await league.OkAsync(league.GetAsync($"auctions/{auctionId}/live"));
        Assert.Equal(2_500_000, state.GetProperty("currentLot").GetProperty("currentBid").GetDecimal());
        Assert.Equal(2, state.GetProperty("bids").GetArrayLength());
        var winner = state.GetProperty("currentLot").GetProperty("currentBidFranchiseId").GetString();
        var winnerPurse = state.GetProperty("franchises").EnumerateArray().First(f => f.GetProperty("franchiseId").GetString() == winner).GetProperty("purseRemaining").GetDecimal();

        var sold = await league.OkAsync(league.PostAsync($"auctions/{auctionId}/close-lot", new { sold = true }));
        var after = sold.GetProperty("franchises").EnumerateArray().First(f => f.GetProperty("franchiseId").GetString() == winner);
        Assert.Equal(winnerPurse - 2_500_000, after.GetProperty("purseRemaining").GetDecimal());
        Assert.Equal(1, sold.GetProperty("sold").GetInt32());

        // Second lot goes unsold, is reopened in round 2 and the auction completes.
        await league.OkAsync(league.PostAsync($"auctions/{auctionId}/open-lot", new { lotId = (string?)null }));
        Assert.Equal(HttpStatusCode.Conflict, (await league.PostAsync($"auctions/{auctionId}/close-lot", new { sold = true })).Status); // no bids
        await league.OkAsync(league.PostAsync($"auctions/{auctionId}/close-lot", new { sold = false }));
        var reopened = await league.OkAsync(league.PostAsync($"auctions/{auctionId}/reopen-unsold", new { lotIds = (string[]?)null }));
        Assert.Equal(1, reopened.GetProperty("reopened").GetInt32());
        await league.OkAsync(league.PostAsync($"auctions/{auctionId}/complete"));
        var results = await league.OkAsync(league.GetAsync($"auctions/{auctionId}/results"));
        Assert.Equal(1, results.GetProperty("sold").GetInt32());
        Assert.Equal(2_500_000, results.GetProperty("totalSpent").GetDecimal());

        await Task.Delay(300);
        lock (events)
        {
            Assert.Contains("bid", events);
            Assert.Contains("sold", events);
        }
        await hub.DisposeAsync();
        Assert.NotNull(seeded);
    }
}

[Collection("api")]
public class SquadRetentionFinanceTests(ApiFactory api)
{
    [Fact]
    public async Task Retention_submission_charges_the_purse_and_releases_the_rest_once()
    {
        var owner = await api.LoginAsync("owner.hyd@demo.local");
        var overview = await owner.OkAsync(owner.GetAsync("retention"));
        var squad = overview.GetProperty("squad").EnumerateArray().ToList();
        var capped = squad.Where(p => p.GetProperty("isCapped").GetBoolean() && !p.GetProperty("isOverseas").GetBoolean()).Take(2).Select(p => p.GetProperty("playerId").GetString()).ToList();
        Assert.Equal(2, capped.Count);
        var tooMany = await owner.OkAsync(owner.PostAsync("retention/simulate", new { playerIds = squad.Take(10).Select(p => p.GetProperty("playerId").GetString()) }));
        Assert.False(tooMany.GetProperty("isValid").GetBoolean());

        var plan = await owner.OkAsync(owner.PostAsync("retention/plans", new { name = "Core two", playerIds = capped }));
        var result = await owner.OkAsync(owner.PostAsync($"retention/plans/{plan.GetProperty("id").GetString()}/submit"));
        Assert.Equal(2, result.GetProperty("slots").GetArrayLength());
        var cost = result.GetProperty("totalCost").GetDecimal();
        Assert.Equal(32 * Money.Crore, cost); // demo rule: first two capped slots ₹18 Cr + ₹14 Cr

        var again = await owner.PostAsync($"retention/plans/{plan.GetProperty("id").GetString()}/submit");
        Assert.Equal(HttpStatusCode.Conflict, again.Status);
        var after = await owner.OkAsync(owner.GetAsync("retention"));
        Assert.Equal(2, after.GetProperty("squad").GetArrayLength()); // everyone else released to the auction pool
        Assert.True(after.GetProperty("hasSubmitted").GetBoolean());
    }

    [Fact]
    public async Task Playing_xi_is_validated_against_season_rules()
    {
        var coach = await api.LoginAsync("head.coach.mum@demo.local");
        var squad = await coach.OkAsync(coach.GetAsync("squad"));
        var players = squad.GetProperty("players").EnumerateArray().ToList();
        var overseas = players.Where(p => p.GetProperty("isOverseas").GetBoolean()).Take(5).ToList();
        var locals = players.Where(p => !p.GetProperty("isOverseas").GetBoolean()).Take(6).ToList();
        var xi = overseas.Concat(locals).Select((p, i) => new { playerId = p.GetProperty("playerId").GetString(), battingOrder = i + 1, isImpactSubstitute = false, isBowlingOption = p.GetProperty("canBowl").GetBoolean() }).ToList();
        var keeper = players.First(p => p.GetProperty("isWicketKeeper").GetBoolean()).GetProperty("playerId").GetString();
        var body = new { name = "Too many overseas", captainId = xi[0].playerId, viceCaptainId = xi[1].playerId, wicketKeeperId = keeper, isPreferred = false, players = xi };
        var validation = await coach.OkAsync(coach.PostAsync("squad/playing-xi/validate", body));
        Assert.Contains(validation.GetProperty("errors").EnumerateArray(), e => e.GetString()!.Contains("overseas"));
        Assert.Equal(HttpStatusCode.BadRequest, (await coach.PostAsync("squad/playing-xi", body)).Status);
    }

    [Fact]
    public async Task Large_expenses_need_approval_rights_and_duplicates_are_refused()
    {
        var finance = await api.LoginAsync("finance.manager.mum@demo.local");
        var big = await finance.PostAsync("finance/expenses", new { description = "Stadium upgrade", amount = 60_000_000, category = "OtherExpense", reference = "INV-1", idempotencyKey = "k-big" });
        Assert.Equal(HttpStatusCode.Forbidden, big.Status);
        await finance.OkAsync(finance.PostAsync("finance/expenses", new { description = "Kit", amount = 500_000, category = "OtherExpense", reference = "INV-2", idempotencyKey = "k-kit" }));
        var dup = await finance.PostAsync("finance/expenses", new { description = "Kit", amount = 500_000, category = "OtherExpense", reference = "INV-2", idempotencyKey = "k-kit" });
        Assert.Equal(HttpStatusCode.Conflict, dup.Status);
        var purse = await finance.PostAsync("finance/expenses", new { description = "Sneaky", amount = 1, category = "AuctionPurse", reference = "x", idempotencyKey = "k-purse" });
        Assert.Equal(HttpStatusCode.BadRequest, purse.Status); // purse entries only come from auctions/retention/trades
    }

    [Fact]
    public async Task Reports_export_as_pdf_excel_and_csv()
    {
        var owner = await api.LoginAsync("owner.mum@demo.local");
        foreach (var (format, type) in new[] { ("pdf", "application/pdf"), ("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"), ("csv", "text/csv") })
        {
            using var res = await owner.Http.GetAsync($"/api/v1/reports/squad/export?format={format}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.StartsWith(type, res.Content.Headers.ContentType!.ToString());
            Assert.True((await res.Content.ReadAsByteArrayAsync()).Length > 200);
        }
        var dash = await owner.OkAsync(owner.GetAsync("reports/dashboard"));
        Assert.Equal(JsonValueKind.Object, dash.GetProperty("owner").ValueKind);
        Assert.Equal(JsonValueKind.Object, dash.GetProperty("finance").ValueKind);
    }

    [Fact]
    public async Task Statistics_carry_their_source_and_analysis_is_computed()
    {
        var analyst = await api.LoginAsync("analyst.mum@demo.local");
        var players = await analyst.OkAsync(analyst.GetAsync("players?sort=runs&desc=true&pageSize=1"));
        var id = players.GetProperty("items")[0].GetProperty("id").GetString();
        var stats = await analyst.OkAsync(analyst.GetAsync($"players/{id}/statistics"));
        Assert.All(stats.GetProperty("careers").EnumerateArray(), c => Assert.Contains("synthetic", c.GetProperty("dataSource").GetString()));
        var analysis = await analyst.OkAsync(analyst.GetAsync($"players/{id}/analysis"));
        Assert.Equal(3, analysis.GetProperty("phases").GetArrayLength());
        Assert.True(analysis.GetProperty("matchesAnalysed").GetInt32() > 0);
        var valuation = await analyst.OkAsync(analyst.GetAsync($"players/{id}/valuation"));
        Assert.Contains("Not an official price", valuation.GetProperty("disclaimer").GetString());
    }
}
