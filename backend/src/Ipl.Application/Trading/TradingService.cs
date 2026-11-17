using System.Data;
using System.Text.Json;
using Ipl.Application.Analytics;
using Ipl.Application.Finance;
using Ipl.Application.Rules;
using Ipl.Application.Squads;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Trading;

public record TradeTermsRequest(
    Guid CounterpartyFranchiseId, IReadOnlyList<Guid> PlayersOffered, IReadOnlyList<Guid> PlayersRequested,
    decimal CashConsideration, Guid? CashPayerFranchiseId, string? Conditions);

public record CreateTradeRequest(TradeTermsRequest Terms, bool Submit, string? Message);

public record CounterTradeRequest(IReadOnlyList<Guid> PlayersOffered, IReadOnlyList<Guid> PlayersRequested, decimal CashConsideration,
    Guid? CashPayerFranchiseId, string? Conditions, string? Message);

public record TradeMessageRequest(string Message, bool Internal);

public record ApprovalDecisionRequest(bool Approve, string? Comment);

public record TradeFranchiseDto(Guid Id, string Name, string ShortName, string PrimaryColor);

public record TradePlayerDto(Guid PlayerId, string Name, string Role, bool IsOverseas, Guid FromFranchiseId, Guid ToFranchiseId, decimal ContractSalary, string Availability);

public record TradeNegotiationDto(int RevisionNo, string Action, string? ByFranchise, string? ByUser, string? Message, bool IsInternal, DateTime At);

public record TradeApprovalDto(Guid Id, string Type, Guid? FranchiseId, string? FranchiseName, Guid? PlayerId, string? PlayerName,
    string Decision, string? DecidedBy, DateTime? DecidedAt, string? Comment, bool CanDecide);

public record TradeSummaryDto(
    Guid Id, string Reference, string Status, string Type, TradeFranchiseDto Initiator, TradeFranchiseDto Counterparty,
    int PlayerCount, decimal CashConsideration, Guid? CashPayerFranchiseId, DateTime? ExpiresAt, DateTime UpdatedAt,
    bool AwaitingMyResponse, int RevisionNo);

public record TradeDetailDto(
    Guid Id, string Reference, string Status, string Type, int SeasonId, TradeFranchiseDto Initiator, TradeFranchiseDto Counterparty,
    IReadOnlyList<TradePlayerDto> Players, decimal CashConsideration, Guid? CashPayerFranchiseId, string? Conditions,
    DateTime? ExpiresAt, DateTime? SubmittedAt, DateTime? CompletedAt, Guid? AwaitingFranchiseId, int RevisionNo,
    IReadOnlyList<TradeNegotiationDto> Negotiations, IReadOnlyList<TradeApprovalDto> Approvals, IReadOnlyList<string> AllowedActions);

public record ImpactLineDto(string Label, string Before, string After, string Change, bool IsWarning);

public record FranchiseImpactDto(
    Guid FranchiseId, string FranchiseName, CompositionDto Before, CompositionDto After,
    decimal SalaryBefore, decimal SalaryAfter, decimal PurseBefore, decimal PurseAfter, decimal TradeConsiderationChange,
    decimal StrengthBefore, decimal StrengthAfter, IReadOnlyList<string> PlayersIn, IReadOnlyList<string> PlayersOut,
    IReadOnlyList<string> Risks, IReadOnlyList<string> Violations);

public record TradeImpactDto(FranchiseImpactDto Initiator, FranchiseImpactDto Counterparty, IReadOnlyList<string> RuleNotes, bool IsValid);

public class TradingService(
    IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, IAuditService audit, INotificationService notify,
    RatingService ratings, IRealtimePublisher realtime)
{
    // ---------------- Queries ----------------

    public async Task<PagedResult<TradeSummaryDto>> ListAsync(string? status, Guid? franchiseId, PageRequest page, CancellationToken ct)
    {
        me.Require(Perm.TradesRead);
        var q = db.TradeOffers.AsNoTracking().AsQueryable();
        if (me.IsLeagueWide)
        {
            // The league sees offers once they leave draft.
            q = q.Where(t => t.Status != TradeStatus.Draft);
            if (franchiseId is { } f) q = q.Where(t => t.InitiatorFranchiseId == f || t.CounterpartyFranchiseId == f);
        }
        else
        {
            var mine = me.RequireFranchise();
            // Drafts are private to the initiator.
            q = q.Where(t => t.InitiatorFranchiseId == mine || (t.CounterpartyFranchiseId == mine && t.Status != TradeStatus.Draft));
        }
        if (status == "open") q = q.Where(t => t.Status != TradeStatus.Completed && t.Status != TradeStatus.Rejected && t.Status != TradeStatus.Withdrawn && t.Status != TradeStatus.Expired);
        else if (status == "closed") q = q.Where(t => t.Status == TradeStatus.Completed || t.Status == TradeStatus.Rejected || t.Status == TradeStatus.Withdrawn || t.Status == TradeStatus.Expired);
        else if (Enum.TryParse<TradeStatus>(status, true, out var st)) q = q.Where(t => t.Status == st);

        var me_ = me.FranchiseId;
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt).Skip((page.SafePage - 1) * page.SafeSize).Take(page.SafeSize)
            .Include(t => t.InitiatorFranchise).Include(t => t.CounterpartyFranchise).Include(t => t.Players).ToListAsync(ct);
        return new PagedResult<TradeSummaryDto>(rows.Select(t => new TradeSummaryDto(t.Id, t.Reference, t.Status.ToString(), t.Type.ToString(),
            F(t.InitiatorFranchise!), F(t.CounterpartyFranchise!), t.Players.Count, t.CashConsideration, t.CashPayerFranchiseId, t.ExpiresAt,
            t.UpdatedAt ?? t.CreatedAt, me_ is not null && t.AwaitingFranchiseId == me_ && t.IsNegotiable, t.RevisionNo)).ToList(),
            page.SafePage, page.SafeSize, total);
    }

    public async Task<TradeDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        me.Require(Perm.TradesRead);
        var t = await LoadAsync(id, ct, tracking: false);
        return await ToDetailAsync(t, ct);
    }

    // ---------------- Proposal & negotiation ----------------

    public async Task<TradeDetailDto> CreateAsync(CreateTradeRequest req, CancellationToken ct)
    {
        me.Require(Perm.TradesCreate);
        var initiator = me.RequireFranchise();
        var season = await rules.CurrentSeasonAsync(ct);
        if (req.Terms.CounterpartyFranchiseId == initiator) throw new AppValidationException("counterpartyFranchiseId", "Choose another franchise.");
        if (!await db.Franchises.AnyAsync(f => f.Id == req.Terms.CounterpartyFranchiseId && f.IsActive, ct))
            throw new AppValidationException("counterpartyFranchiseId", "That franchise does not exist or is inactive.");

        var trade = new TradeOffer
        {
            Reference = await NextReferenceAsync(season.Year, ct), SeasonId = season.Id, InitiatorFranchiseId = initiator,
            CounterpartyFranchiseId = req.Terms.CounterpartyFranchiseId, Status = TradeStatus.Draft, AwaitingFranchiseId = initiator,
        };
        await ApplyTermsAsync(trade, initiator, req.Terms.PlayersOffered, req.Terms.PlayersRequested, req.Terms.CashConsideration,
            req.Terms.CashPayerFranchiseId, req.Terms.Conditions, season.Id, ct);
        db.TradeOffers.Add(trade);
        Log(trade, NegotiationAction.Created, req.Message, false);
        audit.Record("trades.created", "TradeOffer", trade.Id, new { trade.Reference }, initiator);
        if (req.Submit) await SubmitInternalAsync(trade, ct);
        await db.SaveChangesAsync(ct);
        if (req.Submit) await AfterSubmitAsync(trade, ct);
        return await GetAsync(trade.Id, ct);
    }

    public async Task<TradeDetailDto> UpdateDraftAsync(Guid id, TradeTermsRequest terms, CancellationToken ct)
    {
        me.Require(Perm.TradesCreate);
        var t = await LoadAsync(id, ct);
        var mine = me.RequireFranchise();
        if (t.Status != TradeStatus.Draft || t.InitiatorFranchiseId != mine) throw new ConflictException("Only your own drafts can be edited.");
        if (terms.CounterpartyFranchiseId != t.CounterpartyFranchiseId) throw new AppValidationException("counterpartyFranchiseId", "Start a new trade to change the other franchise.");
        await ApplyTermsAsync(t, mine, terms.PlayersOffered, terms.PlayersRequested, terms.CashConsideration, terms.CashPayerFranchiseId, terms.Conditions, t.SeasonId, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TradeDetailDto> SubmitAsync(Guid id, CancellationToken ct)
    {
        me.Require(Perm.TradesCreate);
        var t = await LoadAsync(id, ct);
        if (t.Status != TradeStatus.Draft || t.InitiatorFranchiseId != me.RequireFranchise()) throw new ConflictException("Only your own drafts can be submitted.");
        await SubmitInternalAsync(t, ct);
        await db.SaveChangesAsync(ct);
        await AfterSubmitAsync(t, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TradeDetailDto> CounterAsync(Guid id, CounterTradeRequest req, CancellationToken ct)
    {
        me.Require(Perm.TradesCreate);
        var t = await LoadAsync(id, ct);
        var mine = RequireParty(t);
        if (!t.IsNegotiable || t.Status == TradeStatus.Draft) throw new ConflictException($"A {t.Status} trade cannot be countered.");
        if (t.AwaitingFranchiseId != mine) throw new ConflictException("It's the other franchise's turn to respond.");
        await CheckWindowAsync(t.SeasonId, ct);
        // "Offered" is always from the acting franchise's point of view.
        await ApplyTermsAsync(t, mine, req.PlayersOffered, req.PlayersRequested, req.CashConsideration, req.CashPayerFranchiseId, req.Conditions, t.SeasonId, ct);
        t.RevisionNo++;
        t.Status = TradeStatus.CounterofferReceived;
        t.AwaitingFranchiseId = t.OtherParty(mine);
        t.ExpiresAt = await ExpiryAsync(t.SeasonId, ct);
        Log(t, NegotiationAction.Countered, req.Message, false, snapshot: true);
        audit.Record("trades.countered", "TradeOffer", t.Id, new { t.Reference, t.RevisionNo }, mine);
        await SaveAsync(ct);
        await NotifyOtherAsync(t, mine, "trade.countered", $"Counteroffer on {t.Reference}", $"{await NameAsync(mine, ct)} sent revised terms.", ct);
        return await GetAsync(id, ct);
    }

    public async Task<TradeDetailDto> AcceptAsync(Guid id, string? message, CancellationToken ct)
    {
        me.Require(Perm.TradesCreate);
        var t = await LoadAsync(id, ct);
        var mine = RequireParty(t);
        if (!t.IsNegotiable || t.Status == TradeStatus.Draft) throw new ConflictException($"A {t.Status} trade cannot be accepted.");
        if (t.AwaitingFranchiseId != mine) throw new ConflictException("You cannot accept your own terms; wait for the other franchise.");
        await CheckWindowAsync(t.SeasonId, ct);
        var seasonRules = await rules.ForSeasonAsync(t.SeasonId, ct);

        t.AwaitingFranchiseId = null;
        foreach (var fid in new[] { t.InitiatorFranchiseId, t.CounterpartyFranchiseId })
            t.Approvals.Add(new TradeApproval { Type = ApprovalType.FranchiseAuthorization, FranchiseId = fid, Decision = ApprovalDecision.Pending });
        if (seasonRules.BoolOr(RuleKeys.TradeRequiresPlayerConsent, true))
            foreach (var p in t.Players)
                t.Approvals.Add(new TradeApproval { Type = ApprovalType.PlayerConsent, FranchiseId = p.FromFranchiseId, PlayerId = p.PlayerId, Decision = ApprovalDecision.Pending });
        if (seasonRules.BoolOr(RuleKeys.TradeRequiresLeagueApproval, true))
            t.Approvals.Add(new TradeApproval { Type = ApprovalType.LeagueApproval, Decision = ApprovalDecision.Pending });
        t.Status = TradeStatus.AcceptedInPrinciple;
        t.ExpiresAt = null;
        Log(t, NegotiationAction.Accepted, message, false, snapshot: true);
        audit.Record("trades.accepted_in_principle", "TradeOffer", t.Id, new { t.Reference }, mine);
        await SaveAsync(ct);
        await NotifyBothAsync(t, "trade.accepted", $"{t.Reference} accepted in principle", "Franchise authorisation, player consent and league approval are now required.", ct);
        return await GetAsync(id, ct);
    }

    public async Task<TradeDetailDto> RejectAsync(Guid id, string? message, CancellationToken ct)
    {
        me.Require(Perm.TradesCreate);
        var t = await LoadAsync(id, ct);
        var mine = RequireParty(t);
        if (!t.IsNegotiable || t.Status == TradeStatus.Draft || t.AwaitingFranchiseId != mine) throw new ConflictException("You cannot reject this trade now.");
        t.Status = TradeStatus.Rejected;
        t.AwaitingFranchiseId = null;
        Log(t, NegotiationAction.Rejected, message, false);
        audit.Record("trades.rejected", "TradeOffer", t.Id, new { t.Reference }, mine);
        await SaveAsync(ct);
        await NotifyOtherAsync(t, mine, "trade.rejected", $"{t.Reference} was rejected", message ?? "The other franchise rejected the offer.", ct);
        return await GetAsync(id, ct);
    }

    public async Task<TradeDetailDto> WithdrawAsync(Guid id, string? message, CancellationToken ct)
    {
        me.Require(Perm.TradesCreate);
        var t = await LoadAsync(id, ct);
        var mine = RequireParty(t);
        if (t.Status is TradeStatus.Approved or TradeStatus.Completed || !t.IsOpen) throw new ConflictException($"A {t.Status} trade cannot be withdrawn.");
        if (t.Status == TradeStatus.Draft && t.InitiatorFranchiseId != mine) throw new ConflictException("Only the initiator can discard a draft.");
        t.Status = TradeStatus.Withdrawn;
        t.AwaitingFranchiseId = null;
        Log(t, NegotiationAction.Withdrawn, message, false);
        audit.Record("trades.withdrawn", "TradeOffer", t.Id, new { t.Reference }, mine);
        await SaveAsync(ct);
        if (t.SubmittedAt is not null) await NotifyOtherAsync(t, mine, "trade.withdrawn", $"{t.Reference} was withdrawn", message ?? "The trade was withdrawn.", ct);
        return await GetAsync(id, ct);
    }

    public async Task<TradeDetailDto> AddMessageAsync(Guid id, TradeMessageRequest req, CancellationToken ct)
    {
        me.Require(Perm.TradesRead);
        if (string.IsNullOrWhiteSpace(req.Message)) throw new AppValidationException("message", "Write a message.");
        var t = await LoadAsync(id, ct);
        var mine = RequireParty(t);
        if (!req.Internal) me.Require(Perm.TradesCreate);
        Log(t, req.Internal ? NegotiationAction.Note : NegotiationAction.Message, req.Message.Trim(), req.Internal);
        await SaveAsync(ct);
        if (!req.Internal && t.Status != TradeStatus.Draft)
            await NotifyOtherAsync(t, mine, "trade.message", $"New message on {t.Reference}", req.Message.Length > 120 ? req.Message[..120] + "…" : req.Message, ct);
        return await GetAsync(id, ct);
    }

    // ---------------- Approvals ----------------

    public async Task<TradeDetailDto> DecideApprovalAsync(Guid id, Guid approvalId, ApprovalDecisionRequest req, CancellationToken ct)
    {
        var t = await LoadAsync(id, ct);
        var approval = t.Approvals.FirstOrDefault(a => a.Id == approvalId) ?? throw new NotFoundException("Approval");
        if (approval.Decision != ApprovalDecision.Pending) throw new ConflictException("This approval has already been decided.");
        if (t.Status is not (TradeStatus.AcceptedInPrinciple or TradeStatus.AwaitingPlayerConsent or TradeStatus.AwaitingLeagueApproval))
            throw new ConflictException($"Approvals are closed for a {t.Status} trade.");
        if (!CanDecide(approval, t)) throw new ForbiddenException("You are not allowed to make this decision.");
        // Approvals happen in order: franchises, then players, then the league.
        var expectedStage = t.StatusFromApprovals();
        var stage = approval.Type switch
        {
            ApprovalType.FranchiseAuthorization => TradeStatus.AcceptedInPrinciple,
            ApprovalType.PlayerConsent => TradeStatus.AwaitingPlayerConsent,
            _ => TradeStatus.AwaitingLeagueApproval,
        };
        if (req.Approve && stage != expectedStage) throw new ConflictException("Earlier approvals must be completed first.");
        if (!req.Approve && string.IsNullOrWhiteSpace(req.Comment)) throw new AppValidationException("comment", "Give a reason when rejecting.");

        approval.Decision = req.Approve ? ApprovalDecision.Approved : ApprovalDecision.Rejected;
        approval.DecidedBy = me.UserId;
        approval.DecidedByName = me.Name;
        approval.DecidedAt = clock.UtcNow;
        approval.Comment = req.Comment?.Trim();
        var before = t.Status;
        t.Status = t.StatusFromApprovals();
        Log(t, req.Approve ? NegotiationAction.Approved : NegotiationAction.Rejected,
            $"{approval.Type}{(approval.PlayerId is null ? "" : " (player)")}: {(req.Approve ? "approved" : "rejected")}{(req.Comment is { Length: > 0 } c ? " – " + c : "")}", false);
        audit.Record("trades.approval_decided", "TradeOffer", t.Id,
            new { approval.Type, approval.FranchiseId, approval.PlayerId, Decision = approval.Decision.ToString(), req.Comment }, approval.FranchiseId);
        await SaveAsync(ct);

        if (t.Status == TradeStatus.Rejected)
            await NotifyBothAsync(t, "trade.rejected", $"{t.Reference} was rejected", $"{approval.Type} was rejected: {req.Comment}", ct);
        else if (t.Status != before)
        {
            await NotifyBothAsync(t, "trade.progress", $"{t.Reference}: {Pretty(t.Status)}", $"The trade moved to '{Pretty(t.Status)}'.", ct);
            if (t.Status == TradeStatus.AwaitingLeagueApproval)
                await notify.NotifyLeagueAsync(Perm.TradesLeagueApprove, "trade.league_approval", $"League approval needed: {t.Reference}", "Both franchises and all players have approved.", $"/trading/{t.Id}", ct);
        }
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Makes the transfer official: moves players, contracts and money in one serializable transaction after
    /// re-checking every rule. Only possible once all approvals are recorded.
    /// </summary>
    public async Task<TradeDetailDto> CompleteAsync(Guid id, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var t = await LoadAsync(id, ct);
            var seasonRules = await rules.ForSeasonAsync(t.SeasonId, ct);
            var leagueRequired = seasonRules.BoolOr(RuleKeys.TradeRequiresLeagueApproval, true);
            if (leagueRequired) me.Require(Perm.TradesLeagueApprove);
            else
            {
                me.Require(Perm.TradesApprove);
                RequireParty(t);
            }
            if (t.Status == TradeStatus.Completed) throw new ConflictException("This trade is already complete.");
            if (t.Status != TradeStatus.Approved || t.Approvals.Any(a => a.Decision != ApprovalDecision.Approved))
                throw new ConflictException("Every franchise, player and league approval must be recorded before completion.");
            if (await db.TradeTransactions.AnyAsync(x => x.TradeOfferId == t.Id, ct)) throw new ConflictException("This trade is already complete.");

            var impact = await ImpactAsync(t, ct);
            if (!impact.IsValid)
                throw new DomainException("trade.invalid", "The trade now breaks a rule: " + string.Join(" ", impact.Initiator.Violations.Concat(impact.Counterparty.Violations)));

            var now = clock.UtcNow;
            var season = await db.Seasons.FirstAsync(s => s.Id == t.SeasonId, ct);
            foreach (var tp in t.Players)
            {
                var squad = await db.FranchiseSquads.FirstOrDefaultAsync(s => s.PlayerId == tp.PlayerId && s.SeasonId == t.SeasonId && s.Status == SquadMemberStatus.Active, ct);
                if (squad is null || squad.FranchiseId != tp.FromFranchiseId)
                    throw new ConflictException("A player in this trade is no longer in the expected squad.");
                squad.Status = SquadMemberStatus.TradedOut;
                squad.LeftAt = now;
                // Flush first so the unique "one active squad row per player and season" index never sees two active rows.
                await db.SaveChangesAsync(ct);
                db.FranchiseSquads.Add(new FranchiseSquad
                {
                    FranchiseId = tp.ToFranchiseId, SeasonId = t.SeasonId, PlayerId = tp.PlayerId, Acquisition = AcquisitionType.Trade,
                    Price = tp.ContractSalary, Status = SquadMemberStatus.Active, JoinedAt = now,
                });
                var player = await db.Players.FirstAsync(p => p.Id == tp.PlayerId, ct);
                player.CurrentFranchiseId = tp.ToFranchiseId;
                player.Status = PlayerStatus.Traded;
                var contract = await db.PlayerContracts.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.PlayerId == tp.PlayerId && c.FranchiseId == tp.FromFranchiseId && c.Status == ContractStatus.Active, ct);
                if (contract is not null)
                {
                    contract.Status = ContractStatus.Transferred;
                    db.PlayerContracts.Add(new PlayerContract
                    {
                        PlayerId = tp.PlayerId, FranchiseId = tp.ToFranchiseId, StartSeasonId = t.SeasonId, EndSeasonId = contract.EndSeasonId,
                        StartDate = clock.Today, EndDate = contract.EndDate, AnnualSalary = contract.AnnualSalary, Acquisition = AcquisitionType.Trade,
                        Status = ContractStatus.Active, Notes = $"Transferred via trade {t.Reference}",
                    });
                }
                db.PlayerTeams.Add(new PlayerTeam { PlayerId = tp.PlayerId, FranchiseId = tp.ToFranchiseId, SeasonId = t.SeasonId, Acquisition = AcquisitionType.Trade, Price = tp.ContractSalary, FromDate = clock.Today });
                var oldTeam = await db.PlayerTeams.Where(x => x.PlayerId == tp.PlayerId && x.FranchiseId == tp.FromFranchiseId && x.ToDate == null).ToListAsync(ct);
                foreach (var ot in oldTeam) ot.ToDate = clock.Today;

                if (seasonRules.BoolOr(RuleKeys.TradeSalaryAffectsPurse, false) && tp.ContractSalary > 0)
                {
                    await Ledger.PostAsync(db, tp.FromFranchiseId, t.SeasonId, FinanceCategory.AuctionPurse, FinanceDirection.Credit, tp.ContractSalary,
                        $"Purse released: {player.FullName} traded ({t.Reference})", "TradeOffer", t.Id.ToString(), $"trade:{t.Id}:salary-out:{tp.PlayerId}", me.UserId, now, ct);
                    await Ledger.PostAsync(db, tp.ToFranchiseId, t.SeasonId, FinanceCategory.AuctionPurse, FinanceDirection.Debit, tp.ContractSalary,
                        $"Purse used: {player.FullName} acquired ({t.Reference})", "TradeOffer", t.Id.ToString(), $"trade:{t.Id}:salary-in:{tp.PlayerId}", me.UserId, now, ct);
                }
            }
            if (t.CashConsideration > 0 && t.CashPayerFranchiseId is { } payer)
            {
                var receiver = t.OtherParty(payer);
                await Ledger.PostAsync(db, payer, t.SeasonId, FinanceCategory.TradeConsideration, FinanceDirection.Debit, t.CashConsideration,
                    $"Trade consideration paid ({t.Reference})", "TradeOffer", t.Id.ToString(), $"trade:{t.Id}:cash-out", me.UserId, now, ct);
                await Ledger.PostAsync(db, receiver, t.SeasonId, FinanceCategory.TradeConsideration, FinanceDirection.Credit, t.CashConsideration,
                    $"Trade consideration received ({t.Reference})", "TradeOffer", t.Id.ToString(), $"trade:{t.Id}:cash-in", me.UserId, now, ct);
                if (seasonRules.BoolOr(RuleKeys.TradeConsiderationAffectsPurse, false))
                {
                    await Ledger.PostAsync(db, payer, t.SeasonId, FinanceCategory.AuctionPurse, FinanceDirection.Debit, t.CashConsideration,
                        $"Purse: trade consideration ({t.Reference})", "TradeOffer", t.Id.ToString(), $"trade:{t.Id}:purse-out", me.UserId, now, ct);
                    await Ledger.PostAsync(db, receiver, t.SeasonId, FinanceCategory.AuctionPurse, FinanceDirection.Credit, t.CashConsideration,
                        $"Purse: trade consideration ({t.Reference})", "TradeOffer", t.Id.ToString(), $"trade:{t.Id}:purse-in", me.UserId, now, ct);
                }
            }
            var summary = new
            {
                t.Reference, Season = season.Name,
                Players = t.Players.Select(p => new { p.PlayerId, p.FromFranchiseId, p.ToFranchiseId, p.ContractSalary }),
                t.CashConsideration, t.CashPayerFranchiseId,
            };
            db.TradeTransactions.Add(new TradeTransaction { TradeOfferId = t.Id, SeasonId = t.SeasonId, CompletedAt = now, CompletedBy = me.UserId, Summary = JsonSerializer.Serialize(summary) });
            t.Status = TradeStatus.Completed;
            t.CompletedAt = now;
            Log(t, NegotiationAction.Completed, "Transfer completed and recorded.", false);
            audit.Record("trades.completed", "TradeOffer", t.Id, summary, t.InitiatorFranchiseId);
            audit.Record("trades.completed", "TradeOffer", t.Id, summary, t.CounterpartyFranchiseId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        var done = await LoadAsync(id, ct, tracking: false);
        await NotifyBothAsync(done, "trade.completed", $"{done.Reference} completed", "The transfer is now official.", ct);
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(done, ct);
    }

    /// <summary>Marks unanswered offers as expired (called by the background job).</summary>
    public async Task<int> ExpireOverdueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var overdue = await db.TradeOffers.Include(t => t.Players)
            .Where(t => t.ExpiresAt != null && t.ExpiresAt < now && (t.Status == TradeStatus.PendingResponse || t.Status == TradeStatus.CounterofferReceived || t.Status == TradeStatus.UnderNegotiation))
            .ToListAsync(ct);
        foreach (var t in overdue)
        {
            t.Status = TradeStatus.Expired;
            t.AwaitingFranchiseId = null;
            db.TradeNegotiations.Add(new TradeNegotiation { TradeOfferId = t.Id, RevisionNo = t.RevisionNo, Action = NegotiationAction.Expired, Message = "Offer expired without a response.", At = now });
            await NotifyBothAsync(t, "trade.expired", $"{t.Reference} expired", "The offer expired without a response.", ct);
        }
        await db.SaveChangesAsync(ct);
        return overdue.Count;
    }

    // ---------------- Impact calculator ----------------

    public async Task<TradeImpactDto> PreviewImpactAsync(TradeTermsRequest terms, CancellationToken ct)
    {
        me.Require(Perm.TradesRead);
        var mine = me.RequireFranchise();
        var season = await rules.CurrentSeasonAsync(ct);
        var preview = new TradeOffer { SeasonId = season.Id, InitiatorFranchiseId = mine, CounterpartyFranchiseId = terms.CounterpartyFranchiseId };
        await ApplyTermsAsync(preview, mine, terms.PlayersOffered, terms.PlayersRequested, terms.CashConsideration, terms.CashPayerFranchiseId, terms.Conditions, season.Id, ct, validateOnly: true);
        return await ImpactAsync(preview, ct);
    }

    public async Task<TradeImpactDto> ImpactForTradeAsync(Guid id, CancellationToken ct)
    {
        me.Require(Perm.TradesRead);
        return await ImpactAsync(await LoadAsync(id, ct, tracking: false), ct);
    }

    private async Task<TradeImpactDto> ImpactAsync(TradeOffer t, CancellationToken ct)
    {
        var seasonRules = await rules.ForSeasonAsync(t.SeasonId, ct);
        var salaryMoves = seasonRules.BoolOr(RuleKeys.TradeSalaryAffectsPurse, false);
        var cashMoves = seasonRules.BoolOr(RuleKeys.TradeConsiderationAffectsPurse, false);
        var a = await SideAsync(t, t.InitiatorFranchiseId, seasonRules, salaryMoves, cashMoves, ct);
        var b = await SideAsync(t, t.CounterpartyFranchiseId, seasonRules, salaryMoves, cashMoves, ct);
        List<string> notes =
        [
            salaryMoves ? "Season rule: traded players' contract salaries move between auction purses." : "Season rule: contract salaries do not change auction purses.",
            cashMoves ? "Season rule: the trade consideration is added to/removed from auction purses." : "Season rule: the trade consideration is a separate payment and does not change auction purses.",
        ];
        return new TradeImpactDto(a, b, notes, a.Violations.Count == 0 && b.Violations.Count == 0);
    }

    private async Task<FranchiseImpactDto> SideAsync(TradeOffer t, Guid franchiseId, SeasonRules seasonRules, bool salaryMoves, bool cashMoves, CancellationToken ct)
    {
        var today = clock.Today;
        var squad = await SquadMath.LoadAsync(db, franchiseId, t.SeasonId, today, ct);
        var outgoing = t.Players.Where(p => p.FromFranchiseId == franchiseId).Select(p => p.PlayerId).ToHashSet();
        var incomingIds = t.Players.Where(p => p.ToFranchiseId == franchiseId).Select(p => p.PlayerId).ToList();
        var incoming = new List<SquadMember>();
        foreach (var other in t.Players.Where(p => p.ToFranchiseId == franchiseId).GroupBy(p => p.FromFranchiseId))
        {
            var otherSquad = await SquadMath.LoadAsync(db, other.Key, t.SeasonId, today, ct);
            incoming.AddRange(otherSquad.Where(m => incomingIds.Contains(m.PlayerId)));
        }
        var after = squad.Where(m => !outgoing.Contains(m.PlayerId)).Concat(incoming).ToList();

        var fs = await db.FranchiseSeasons.AsNoTracking().FirstOrDefaultAsync(s => s.FranchiseId == franchiseId && s.SeasonId == t.SeasonId, ct);
        var purse = fs?.PurseRemaining ?? 0;
        var salaryOut = squad.Where(m => outgoing.Contains(m.PlayerId)).Sum(m => m.Salary);
        var salaryIn = incoming.Sum(m => m.Salary);
        var cash = t.CashPayerFranchiseId == null ? 0 : t.CashPayerFranchiseId == franchiseId ? -t.CashConsideration : t.CashConsideration;
        var purseAfter = purse + (salaryMoves ? salaryOut - salaryIn : 0) + (cashMoves ? cash : 0);

        var allIds = squad.Select(m => m.PlayerId).Concat(incomingIds).Distinct().ToList();
        var rated = await ratings.RateManyAsync(allIds, ct);
        var maxOs = seasonRules.Int(RuleKeys.MaxOverseasInXI);
        var minBowl = seasonRules.IntOr(RuleKeys.MinBowlingOptions, 5);
        decimal Strength(IEnumerable<SquadMember> s) => RatingService.TeamStrength(
            s.Select(m => (m.PlayerId, rated.TryGetValue(m.PlayerId, out var r) ? r.Rating : 40m, m.IsOverseas, m.IsWicketKeeper, m.CanBowl)).ToList(), maxOs, minBowl).Strength;

        var before = SquadMath.Compose(squad, seasonRules);
        var afterComp = SquadMath.Compose(after, seasonRules);
        List<string> violations = [];
        if (after.Count > afterComp.MaxSquad) violations.Add($"Squad would have {after.Count} players (max {afterComp.MaxSquad}).");
        if (afterComp.Overseas > afterComp.MaxOverseas) violations.Add($"Squad would have {afterComp.Overseas} overseas players (max {afterComp.MaxOverseas}).");
        if (purseAfter < 0) violations.Add($"Auction purse would be negative ({Money.Format(purseAfter)}).");
        if (t.Players.Any(p => p.FromFranchiseId == franchiseId) && t.Players.Where(p => p.FromFranchiseId == franchiseId).Any(p => squad.All(m => m.PlayerId != p.PlayerId)))
            violations.Add("A player listed as leaving is not in this squad any more.");

        List<string> risks = [];
        foreach (var m in incoming)
        {
            if (m.Availability != nameof(AvailabilityStatus.Available)) risks.Add($"{m.Name} is currently {m.Availability}.");
            if (m.Age >= 34) risks.Add($"{m.Name} is {m.Age}: shorter remaining career.");
        }
        var otherOpen = await db.TradeOfferPlayers.AsNoTracking()
            .Where(p => incomingIds.Contains(p.PlayerId) && p.TradeOfferId != t.Id && p.TradeOffer!.Status != TradeStatus.Completed
                && p.TradeOffer.Status != TradeStatus.Rejected && p.TradeOffer.Status != TradeStatus.Withdrawn && p.TradeOffer.Status != TradeStatus.Expired && p.TradeOffer.Status != TradeStatus.Draft)
            .Select(p => p.Player!.FullName).Distinct().ToListAsync(ct);
        risks.AddRange(otherOpen.Select(n => $"{n} is also part of another open trade."));
        if (afterComp.WicketKeepers == 0) risks.Add("No wicketkeeper after the trade.");

        var name = await NameAsync(franchiseId, ct);
        return new FranchiseImpactDto(franchiseId, name, before, afterComp, squad.Sum(m => m.Salary), after.Sum(m => m.Salary), purse, purseAfter,
            cash, Strength(squad), Strength(after), incoming.Select(m => m.Name).ToList(),
            squad.Where(m => outgoing.Contains(m.PlayerId)).Select(m => m.Name).ToList(), risks, violations);
    }

    // ---------------- Helpers ----------------

    private async Task ApplyTermsAsync(TradeOffer t, Guid actingFranchise, IReadOnlyList<Guid> offered, IReadOnlyList<Guid> requested,
        decimal cash, Guid? cashPayer, string? conditions, int seasonId, CancellationToken ct, bool validateOnly = false)
    {
        var other = t.OtherParty(actingFranchise);
        if (offered.Count + requested.Count == 0) throw new AppValidationException("players", "A trade must include at least one player.");
        if (offered.Intersect(requested).Any()) throw new AppValidationException("players", "A player cannot be on both sides.");
        if (offered.Count + requested.Count > 10) throw new AppValidationException("players", "A trade can include at most 10 players.");
        if (cash < 0) throw new AppValidationException("cashConsideration", "The trade consideration cannot be negative.");
        if (cash > 0 && cashPayer is null) throw new AppValidationException("cashPayerFranchiseId", "Choose which franchise pays the trade consideration.");
        if (cashPayer is { } payer && payer != t.InitiatorFranchiseId && payer != t.CounterpartyFranchiseId)
            throw new AppValidationException("cashPayerFranchiseId", "The payer must be one of the two franchises.");

        var ids = offered.Concat(requested).ToList();
        var squads = await db.FranchiseSquads.AsNoTracking()
            .Where(s => ids.Contains(s.PlayerId) && s.SeasonId == seasonId && s.Status == SquadMemberStatus.Active)
            .Select(s => new { s.PlayerId, s.FranchiseId, s.Price, s.Player!.FullName })
            .ToListAsync(ct);
        var salaries = await db.PlayerContracts.IgnoreQueryFilters().AsNoTracking()
            .Where(c => ids.Contains(c.PlayerId) && c.Status == ContractStatus.Active)
            .Select(c => new { c.PlayerId, c.FranchiseId, c.AnnualSalary }).ToListAsync(ct);
        foreach (var pid in offered)
            if (squads.FirstOrDefault(s => s.PlayerId == pid)?.FranchiseId != actingFranchise)
                throw new AppValidationException("playersOffered", "You can only offer players in your current squad.");
        foreach (var pid in requested)
            if (squads.FirstOrDefault(s => s.PlayerId == pid)?.FranchiseId != other)
                throw new AppValidationException("playersRequested", "You can only request players in the other franchise's current squad.");

        if (!validateOnly) db.TradeOfferPlayers.RemoveRange(t.Players);
        t.Players = ids.Select(pid =>
        {
            var s = squads.First(x => x.PlayerId == pid);
            var salary = salaries.FirstOrDefault(c => c.PlayerId == pid && c.FranchiseId == s.FranchiseId)?.AnnualSalary ?? s.Price;
            return new TradeOfferPlayer { TradeOfferId = t.Id, PlayerId = pid, FromFranchiseId = s.FranchiseId, ToFranchiseId = s.FranchiseId == actingFranchise ? other : actingFranchise, ContractSalary = salary };
        }).ToList();
        if (!validateOnly) db.TradeOfferPlayers.AddRange(t.Players);
        t.CashConsideration = cash;
        t.CashPayerFranchiseId = cash > 0 ? cashPayer : null;
        t.Conditions = string.IsNullOrWhiteSpace(conditions) ? null : conditions.Trim();
    }

    private async Task SubmitInternalAsync(TradeOffer t, CancellationToken ct)
    {
        await CheckWindowAsync(t.SeasonId, ct);
        t.Status = TradeStatus.PendingResponse;
        t.SubmittedAt = clock.UtcNow;
        t.AwaitingFranchiseId = t.CounterpartyFranchiseId;
        t.ExpiresAt = await ExpiryAsync(t.SeasonId, ct);
        Log(t, NegotiationAction.Submitted, null, false, snapshot: true);
        audit.Record("trades.submitted", "TradeOffer", t.Id, new { t.Reference }, t.InitiatorFranchiseId);
    }

    private async Task AfterSubmitAsync(TradeOffer t, CancellationToken ct)
    {
        await notify.NotifyFranchiseAsync(t.CounterpartyFranchiseId, "trade.received", $"New trade offer {t.Reference}",
            $"{await NameAsync(t.InitiatorFranchiseId, ct)} sent a trade offer.", $"/trading/{t.Id}", [Perm.TradesRead], ct: ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task CheckWindowAsync(int seasonId, CancellationToken ct)
    {
        var seasonRules = await rules.ForSeasonAsync(seasonId, ct);
        var today = clock.Today;
        if (seasonRules.DateOrNull(RuleKeys.TradeWindowOpen) is { } open && today < open) throw new DomainException("trade.window", $"The trade window opens on {open:d MMM yyyy}.");
        if (seasonRules.DateOrNull(RuleKeys.TradeWindowClose) is { } close && today > close) throw new DomainException("trade.window", $"The trade window closed on {close:d MMM yyyy}.");
    }

    private async Task<DateTime> ExpiryAsync(int seasonId, CancellationToken ct) =>
        clock.UtcNow.AddDays((await rules.ForSeasonAsync(seasonId, ct)).IntOr(RuleKeys.TradeOfferExpiryDays, 7));

    private async Task<TradeOffer> LoadAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = db.TradeOffers.Include(t => t.Players).Include(t => t.Negotiations).Include(t => t.Approvals)
            .Include(t => t.InitiatorFranchise).Include(t => t.CounterpartyFranchise).AsSplitQuery();
        var t = await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Trade");
        var visible = me.IsLeagueWide ? t.Status != TradeStatus.Draft
            : me.FranchiseId == t.InitiatorFranchiseId || (me.FranchiseId == t.CounterpartyFranchiseId && t.Status != TradeStatus.Draft);
        if (!visible) throw new NotFoundException("Trade"); // don't reveal other franchises' negotiations
        return t;
    }

    private Guid RequireParty(TradeOffer t)
    {
        var mine = me.RequireFranchise();
        if (!t.IsParty(mine)) throw new ForbiddenException("Only the two franchises in this trade can do that.");
        return mine;
    }

    private bool CanDecide(TradeApproval a, TradeOffer t) => a.Type switch
    {
        ApprovalType.FranchiseAuthorization => me.Has(Perm.TradesApprove) && me.FranchiseId == a.FranchiseId,
        ApprovalType.PlayerConsent => me.Has(Perm.TradesConsent) && me.FranchiseId == a.FranchiseId,
        ApprovalType.LeagueApproval => me.Has(Perm.TradesLeagueApprove) && me.IsLeagueWide,
        _ => false,
    };

    private void Log(TradeOffer t, NegotiationAction action, string? message, bool isInternal, bool snapshot = false)
    {
        var entry = new TradeNegotiation
        {
            TradeOfferId = t.Id, RevisionNo = t.RevisionNo, Action = action, ByFranchiseId = me.FranchiseId, ByUserId = me.UserId,
            ByUserName = me.Name, Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(), IsInternal = isInternal, At = clock.UtcNow,
            TermsSnapshot = snapshot ? JsonSerializer.Serialize(new
            {
                Players = t.Players.Select(p => new { p.PlayerId, p.FromFranchiseId, p.ToFranchiseId, p.ContractSalary }),
                t.CashConsideration, t.CashPayerFranchiseId, t.Conditions,
            }) : null,
        };
        db.TradeNegotiations.Add(entry);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Someone else changed this trade at the same time. Reload and try again."); }
    }

    private async Task NotifyOtherAsync(TradeOffer t, Guid actor, string type, string title, string body, CancellationToken ct)
    {
        await notify.NotifyFranchiseAsync(t.OtherParty(actor), type, title, body, $"/trading/{t.Id}", [Perm.TradesRead], ct: ct);
        await realtime.ToFranchise(t.OtherParty(actor), "trade.updated", new { t.Id, Status = t.Status.ToString() });
        await realtime.ToFranchise(actor, "trade.updated", new { t.Id, Status = t.Status.ToString() });
        await db.SaveChangesAsync(ct);
    }

    private async Task NotifyBothAsync(TradeOffer t, string type, string title, string body, CancellationToken ct)
    {
        foreach (var f in new[] { t.InitiatorFranchiseId, t.CounterpartyFranchiseId })
        {
            await notify.NotifyFranchiseAsync(f, type, title, body, $"/trading/{t.Id}", [Perm.TradesRead], ct: ct);
            await realtime.ToFranchise(f, "trade.updated", new { t.Id, Status = t.Status.ToString() });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> NameAsync(Guid franchiseId, CancellationToken ct) =>
        await db.Franchises.Where(f => f.Id == franchiseId).Select(f => f.Name).FirstOrDefaultAsync(ct) ?? "A franchise";

    private async Task<string> NextReferenceAsync(int year, CancellationToken ct)
    {
        var count = await db.TradeOffers.CountAsync(t => t.Reference.StartsWith($"TR-{year}-"), ct);
        string reference;
        do reference = $"TR-{year}-{++count:0000}";
        while (await db.TradeOffers.AnyAsync(t => t.Reference == reference, ct));
        return reference;
    }

    private static string Pretty(TradeStatus s) => System.Text.RegularExpressions.Regex.Replace(s.ToString(), "(?<!^)([A-Z])", " $1");

    private static TradeFranchiseDto F(Franchise f) => new(f.Id, f.Name, f.ShortName, f.PrimaryColor);

    private async Task<TradeDetailDto> ToDetailAsync(TradeOffer t, CancellationToken ct)
    {
        var ids = t.Players.Select(p => p.PlayerId).Concat(t.Approvals.Where(a => a.PlayerId != null).Select(a => a.PlayerId!.Value)).Distinct().ToList();
        var players = await db.Players.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, Role = p.PrimaryRole!.Name, p.IsOverseas }).ToDictionaryAsync(p => p.Id, ct);
        var avail = await db.PlayerAvailability.AsNoTracking().Where(a => ids.Contains(a.PlayerId)).ToDictionaryAsync(a => a.PlayerId, a => a.Status.ToString(), ct);
        var franchiseNames = new Dictionary<Guid, string> { [t.InitiatorFranchiseId] = t.InitiatorFranchise!.Name, [t.CounterpartyFranchiseId] = t.CounterpartyFranchise!.Name };
        var mine = me.FranchiseId;
        var negotiations = t.Negotiations
            .Where(n => !n.IsInternal || (mine is not null && n.ByFranchiseId == mine))
            .OrderBy(n => n.At)
            .Select(n => new TradeNegotiationDto(n.RevisionNo, n.Action.ToString(), n.ByFranchiseId is { } bf ? franchiseNames.GetValueOrDefault(bf, "League") : "System",
                n.ByUserName, n.Message, n.IsInternal, n.At)).ToList();
        var approvals = t.Approvals.OrderBy(a => a.Type).ThenBy(a => a.FranchiseId)
            .Select(a => new TradeApprovalDto(a.Id, a.Type.ToString(), a.FranchiseId, a.FranchiseId is { } f ? franchiseNames.GetValueOrDefault(f) : null,
                a.PlayerId, a.PlayerId is { } p && players.TryGetValue(p, out var pl) ? pl.FullName : null, a.Decision.ToString(), a.DecidedByName,
                a.DecidedAt, a.Comment, a.Decision == ApprovalDecision.Pending && CanDecide(a, t))).ToList();

        List<string> actions = [];
        if (me.Has(Perm.TradesCreate) && mine is not null && t.IsParty(mine.Value))
        {
            if (t.Status == TradeStatus.Draft && t.InitiatorFranchiseId == mine) actions.AddRange(["edit", "submit", "withdraw"]);
            else if (t.IsNegotiable && t.AwaitingFranchiseId == mine) actions.AddRange(["accept", "counter", "reject", "withdraw"]);
            else if (t.IsNegotiable || t.Status is TradeStatus.AcceptedInPrinciple or TradeStatus.AwaitingPlayerConsent or TradeStatus.AwaitingLeagueApproval) actions.Add("withdraw");
            if (t.Status != TradeStatus.Draft && t.IsOpen) actions.Add("message");
        }
        if (mine is not null && t.IsParty(mine.Value) && me.Has(Perm.TradesRead)) actions.Add("note");
        if (t.Status == TradeStatus.Approved && (me.Has(Perm.TradesLeagueApprove) || me.Has(Perm.TradesApprove) && mine is not null && t.IsParty(mine.Value)))
            actions.Add("complete");

        return new TradeDetailDto(t.Id, t.Reference, t.Status.ToString(), t.Type.ToString(), t.SeasonId, F(t.InitiatorFranchise), F(t.CounterpartyFranchise),
            t.Players.Select(p => new TradePlayerDto(p.PlayerId, players.TryGetValue(p.PlayerId, out var pl) ? pl.FullName : "?", pl?.Role ?? "",
                pl?.IsOverseas ?? false, p.FromFranchiseId, p.ToFranchiseId, p.ContractSalary, avail.GetValueOrDefault(p.PlayerId, "Available"))).ToList(),
            t.CashConsideration, t.CashPayerFranchiseId, t.Conditions, t.ExpiresAt, t.SubmittedAt, t.CompletedAt, t.AwaitingFranchiseId, t.RevisionNo,
            negotiations, approvals, actions.Distinct().ToList());
    }
}
