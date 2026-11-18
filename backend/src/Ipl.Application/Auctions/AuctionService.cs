using System.Data;
using Ipl.Application.Finance;
using Ipl.Application.Rules;
using Ipl.Application.Squads;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Auctions;

public record AuctionSummaryDto(Guid Id, string Name, int SeasonId, string Season, bool IsMegaAuction, string Status, DateTime? ScheduledAt,
    int TotalLots, int Sold, int Unsold, int Pending, decimal TotalSpent);

public record LotDto(
    Guid Id, Guid PlayerId, string PlayerName, string Role, string Country, bool IsOverseas, bool IsCapped, int? Age, string SetName, int SetNumber,
    int LotNumber, decimal BasePrice, string Status, decimal? CurrentBid, Guid? CurrentBidFranchiseId, string? CurrentBidFranchise,
    DateTime? LastBidAt, int Round, string? PhotoUrl);

public record FranchisePurseDto(Guid FranchiseId, string Name, string ShortName, string PrimaryColor, decimal PurseRemaining,
    int SquadSize, int Overseas, int SlotsRemaining, int OverseasSlotsRemaining, int Bought, decimal Spent, decimal MaxBidAllowed);

public record BidDto(Guid Id, Guid FranchiseId, string Franchise, decimal Amount, DateTime PlacedAt, string? PlacedBy);

public record LiveAuctionDto(
    Guid Id, string Name, string Status, int Round, int BidTimerSeconds, bool AllowReopenUnsold, LotDto? CurrentLot, decimal? NextBid,
    IReadOnlyList<BidDto> Bids, IReadOnlyList<FranchisePurseDto> Franchises, int Remaining, int Sold, int Unsold, DateTime ServerTime,
    MyAuctionStateDto? Me);

public record MyAuctionStateDto(Guid FranchiseId, bool CanBid, string? CannotBidReason, decimal? NextBid);

public record CreateAuctionRequest(string Name, bool IsMegaAuction, DateTime? ScheduledAt, int BidTimerSeconds, bool AllowReopenUnsold);

public record AddLotsRequest(IReadOnlyList<Guid> PlayerIds, string SetName, int SetNumber, decimal BasePrice);

public record PlaceBidRequest(Guid LotId, decimal Amount, string IdempotencyKey);

public record ResultRowDto(Guid PlayerId, string Player, string Role, bool IsOverseas, string Outcome, string? Franchise, decimal? Price, decimal BasePrice, int Round);

public record FranchiseResultDto(Guid FranchiseId, string Franchise, int Players, decimal Spent, decimal PurseRemaining, decimal? AveragePrice, IReadOnlyList<ResultRowDto> Bought);

public record AuctionResultsDto(Guid AuctionId, string Name, string Status, int Sold, int Unsold, decimal TotalSpent, decimal? AveragePrice,
    ResultRowDto? HighestPurchase, IReadOnlyList<ResultRowDto> SoldPlayers, IReadOnlyList<ResultRowDto> UnsoldPlayers, IReadOnlyList<FranchiseResultDto> ByFranchise);

public class AuctionService(IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, IAuditService audit, IRealtimePublisher realtime, INotificationService notify)
{
    // ---------------- Configuration ----------------

    public async Task<IReadOnlyList<AuctionSummaryDto>> ListAsync(CancellationToken ct)
    {
        me.Require(Perm.AuctionsRead);
        return await db.AuctionEvents.AsNoTracking().OrderByDescending(a => a.ScheduledAt ?? a.CreatedAt)
            .Select(a => new AuctionSummaryDto(a.Id, a.Name, a.SeasonId, a.Season!.Name, a.IsMegaAuction, a.Status.ToString(), a.ScheduledAt,
                a.Lots.Count, a.Lots.Count(l => l.Status == AuctionLotStatus.Sold), a.Lots.Count(l => l.Status == AuctionLotStatus.Unsold),
                a.Lots.Count(l => l.Status == AuctionLotStatus.Pending || l.Status == AuctionLotStatus.OnBlock),
                db.AuctionResults.Where(r => r.AuctionEventId == a.Id && r.Outcome == AuctionOutcome.Sold).Sum(r => r.Price ?? 0)))
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(CreateAuctionRequest req, CancellationToken ct)
    {
        me.Require(Perm.AuctionsManage);
        if (string.IsNullOrWhiteSpace(req.Name)) throw new AppValidationException("name", "Name the auction.");
        if (req.BidTimerSeconds is < 0 or > 300) throw new AppValidationException("bidTimerSeconds", "Use 0–300 seconds.");
        var season = await rules.CurrentSeasonAsync(ct);
        var a = new AuctionEvent
        {
            SeasonId = season.Id, Name = req.Name.Trim(), IsMegaAuction = req.IsMegaAuction, ScheduledAt = req.ScheduledAt,
            BidTimerSeconds = req.BidTimerSeconds, AllowReopenUnsold = req.AllowReopenUnsold, Status = AuctionStatus.Draft,
        };
        db.AuctionEvents.Add(a);
        audit.Record("auctions.created", "AuctionEvent", a.Id, req);
        await db.SaveChangesAsync(ct);
        return a.Id;
    }

    public async Task UpdateAsync(Guid id, CreateAuctionRequest req, CancellationToken ct)
    {
        me.Require(Perm.AuctionsManage);
        var a = await db.AuctionEvents.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Auction");
        if (a.Status is AuctionStatus.Completed) throw new ConflictException("A completed auction cannot be changed.");
        a.Name = req.Name.Trim();
        a.IsMegaAuction = req.IsMegaAuction;
        a.ScheduledAt = req.ScheduledAt;
        a.BidTimerSeconds = Math.Clamp(req.BidTimerSeconds, 0, 300);
        a.AllowReopenUnsold = req.AllowReopenUnsold;
        audit.Record("auctions.updated", "AuctionEvent", id, req);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LotDto>> LotsAsync(Guid auctionId, CancellationToken ct)
    {
        me.Require(Perm.AuctionsRead);
        return await LotQuery(auctionId).ToListAsync(ct);
    }

    public async Task<int> AddLotsAsync(Guid auctionId, AddLotsRequest req, CancellationToken ct)
    {
        me.Require(Perm.AuctionsManage);
        var a = await db.AuctionEvents.FirstOrDefaultAsync(x => x.Id == auctionId, ct) ?? throw new NotFoundException("Auction");
        if (a.Status is AuctionStatus.Completed) throw new ConflictException("The auction is complete.");
        var seasonRules = await rules.ForSeasonAsync(a.SeasonId, ct);
        if (req.BasePrice < seasonRules.Decimal(RuleKeys.MinBasePrice))
            throw new AppValidationException("basePrice", $"The minimum base price is {Money.Format(seasonRules.Decimal(RuleKeys.MinBasePrice))}.");
        if (string.IsNullOrWhiteSpace(req.SetName)) throw new AppValidationException("setName", "Name the set (e.g. Marquee, Batters 1).");
        var eligible = await db.Players.Where(p => req.PlayerIds.Contains(p.Id) && p.CurrentFranchiseId == null && p.Status == PlayerStatus.AvailableForAuction)
            .Select(p => p.Id).ToListAsync(ct);
        var already = await db.AuctionPlayers.Where(l => l.AuctionEventId == auctionId && req.PlayerIds.Contains(l.PlayerId)).Select(l => l.PlayerId).ToListAsync(ct);
        var lotNo = await db.AuctionPlayers.Where(l => l.AuctionEventId == auctionId).MaxAsync(l => (int?)l.LotNumber, ct) ?? 0;
        var added = 0;
        foreach (var pid in eligible.Except(already))
        {
            db.AuctionPlayers.Add(new AuctionPlayer
            {
                AuctionEventId = auctionId, PlayerId = pid, SetName = req.SetName.Trim(), SetNumber = req.SetNumber, LotNumber = ++lotNo,
                BasePrice = req.BasePrice, Status = AuctionLotStatus.Pending,
            });
            added++;
        }
        audit.Record("auctions.lots_added", "AuctionEvent", auctionId, new { added, req.SetName, req.BasePrice });
        await db.SaveChangesAsync(ct);
        return added;
    }

    /// <summary>Adds every unsigned player marked "available for auction" who isn't in the pool yet.</summary>
    public async Task<int> ImportEligibleAsync(Guid auctionId, decimal basePrice, CancellationToken ct)
    {
        me.Require(Perm.AuctionsManage);
        var ids = await db.Players.Where(p => p.CurrentFranchiseId == null && p.Status == PlayerStatus.AvailableForAuction)
            .Where(p => !db.AuctionPlayers.Any(l => l.AuctionEventId == auctionId && l.PlayerId == p.Id))
            .OrderByDescending(p => p.IsCapped).ThenBy(p => p.PrimaryRole!.SortOrder).Select(p => new { p.Id, p.IsCapped, Role = p.PrimaryRole!.Name }).ToListAsync(ct);
        var total = 0;
        var setNo = await db.AuctionPlayers.Where(l => l.AuctionEventId == auctionId).MaxAsync(l => (int?)l.SetNumber, ct) ?? 0;
        foreach (var g in ids.GroupBy(p => (p.IsCapped, p.Role)))
            total += await AddLotsAsync(auctionId, new AddLotsRequest(g.Select(p => p.Id).ToList(), $"{(g.Key.IsCapped ? "Capped" : "Uncapped")} {g.Key.Role}s", ++setNo, basePrice), ct);
        return total;
    }

    public async Task RemoveLotAsync(Guid auctionId, Guid lotId, CancellationToken ct)
    {
        me.Require(Perm.AuctionsManage);
        var lot = await db.AuctionPlayers.FirstOrDefaultAsync(l => l.Id == lotId && l.AuctionEventId == auctionId, ct) ?? throw new NotFoundException("Lot");
        if (lot.Status != AuctionLotStatus.Pending) throw new ConflictException("Only pending lots can be removed.");
        db.AuctionPlayers.Remove(lot);
        audit.Record("auctions.lot_removed", "AuctionEvent", auctionId, new { lot.PlayerId });
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Running the auction ----------------

    public async Task<LiveAuctionDto> SetStatusAsync(Guid auctionId, string action, CancellationToken ct)
    {
        me.Require(Perm.AuctionsManage);
        var a = await db.AuctionEvents.Include(x => x.Lots).FirstOrDefaultAsync(x => x.Id == auctionId, ct) ?? throw new NotFoundException("Auction");
        var now = clock.UtcNow;
        switch (action)
        {
            case "ready" when a.Status == AuctionStatus.Draft:
                if (a.Lots.Count == 0) throw new ConflictException("Add players to the pool first.");
                a.Status = AuctionStatus.Ready;
                break;
            case "start" when a.Status is AuctionStatus.Ready or AuctionStatus.Draft:
                if (a.Lots.Count == 0) throw new ConflictException("Add players to the pool first.");
                if (await db.AuctionEvents.AnyAsync(x => x.Id != a.Id && (x.Status == AuctionStatus.Live || x.Status == AuctionStatus.Paused), ct))
                    throw new ConflictException("Another auction is already running.");
                a.Status = AuctionStatus.Live;
                a.StartedAt = now;
                break;
            case "pause" when a.Status == AuctionStatus.Live:
                a.Status = AuctionStatus.Paused;
                break;
            case "resume" when a.Status == AuctionStatus.Paused:
                a.Status = AuctionStatus.Live;
                break;
            case "complete" when a.Status is AuctionStatus.Live or AuctionStatus.Paused:
                if (a.Lots.Any(l => l.Status == AuctionLotStatus.OnBlock)) throw new ConflictException("Close the current lot first.");
                a.Status = AuctionStatus.Completed;
                a.CompletedAt = now;
                a.CurrentLotId = null;
                break;
            default:
                throw new ConflictException($"Cannot {action} an auction that is {a.Status}.");
        }
        audit.Record($"auctions.{action}", "AuctionEvent", a.Id, null);
        await SaveAsync(ct);
        await BroadcastAsync(a.Id, "status", new { Status = a.Status.ToString() });
        if (action == "start")
            foreach (var fid in await db.Franchises.Where(f => f.IsActive).Select(f => f.Id).ToListAsync(ct))
                await notify.NotifyFranchiseAsync(fid, "auction.started", $"{a.Name} is live", "Bidding has started.", $"/auction/{a.Id}/live", [Perm.AuctionsBid], ct: ct);
        await db.SaveChangesAsync(ct);
        return await LiveAsync(a.Id, ct);
    }

    /// <summary>Puts a lot on the block: the given one, or the next pending lot in set/lot order.</summary>
    public async Task<LiveAuctionDto> OpenLotAsync(Guid auctionId, Guid? lotId, CancellationToken ct)
    {
        me.Require(Perm.AuctionsManage);
        var a = await db.AuctionEvents.FirstOrDefaultAsync(x => x.Id == auctionId, ct) ?? throw new NotFoundException("Auction");
        if (a.Status != AuctionStatus.Live) throw new ConflictException("Start or resume the auction first.");
        if (await db.AuctionPlayers.AnyAsync(l => l.AuctionEventId == auctionId && l.Status == AuctionLotStatus.OnBlock, ct))
            throw new ConflictException("Close the current lot (sold or unsold) first.");
        var lot = lotId is { } id
            ? await db.AuctionPlayers.FirstOrDefaultAsync(l => l.Id == id && l.AuctionEventId == auctionId, ct) ?? throw new NotFoundException("Lot")
            : await db.AuctionPlayers.Where(l => l.AuctionEventId == auctionId && l.Status == AuctionLotStatus.Pending)
                .OrderBy(l => l.Round).ThenBy(l => l.SetNumber).ThenBy(l => l.LotNumber).FirstOrDefaultAsync(ct)
              ?? throw new ConflictException("No pending lots are left. Reopen unsold players or complete the auction.");
        if (lot.Status != AuctionLotStatus.Pending) throw new ConflictException("That lot is not pending.");
        lot.Status = AuctionLotStatus.OnBlock;
        lot.OpenedAt = clock.UtcNow;
        lot.CurrentBid = null;
        lot.CurrentBidFranchiseId = null;
        lot.LastBidAt = null;
        a.CurrentLotId = lot.Id;
        audit.Record("auctions.lot_opened", "AuctionEvent", auctionId, new { lot.PlayerId, lot.BasePrice });
        await SaveAsync(ct);
        await BroadcastAsync(auctionId, "lotOpened", new { LotId = lot.Id });
        return await LiveAsync(auctionId, ct);
    }

    /// <summary>
    /// Places a bid. Runs in a transaction holding an update lock on the lot, so two franchises cannot both win
    /// the same increment; every rule is checked on the server.
    /// </summary>
    public async Task<LiveAuctionDto> BidAsync(Guid auctionId, PlaceBidRequest req, CancellationToken ct)
    {
        me.Require(Perm.AuctionsBid);
        var fid = me.RequireFranchise();
        if (string.IsNullOrWhiteSpace(req.IdempotencyKey) || req.IdempotencyKey.Length > 64)
            throw new AppValidationException("idempotencyKey", "A unique request key is required.");
        if (await db.AuctionBids.AnyAsync(b => b.FranchiseId == fid && b.IdempotencyKey == req.IdempotencyKey, ct))
            return await LiveAsync(auctionId, ct); // retried request: the bid was already placed

        decimal amount = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var lot = await db.AuctionPlayers
                .FromSqlInterpolated($"SELECT * FROM [ipl].[AuctionPlayers] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {req.LotId}")
                .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Lot");
            var a = await db.AuctionEvents.AsNoTracking().FirstAsync(x => x.Id == lot.AuctionEventId, ct);
            if (a.Id != auctionId) throw new NotFoundException("Lot");
            var check = await CheckBidAsync(a, lot, fid, ct);
            if (check.Reason is not null) throw new DomainException("auction.bid_rejected", check.Reason);
            if (req.Amount != check.NextBid)
                throw new DomainException("auction.bid_amount", $"The next valid bid is {Money.Format(check.NextBid!.Value)}.");
            amount = req.Amount;
            var now = clock.UtcNow;
            lot.CurrentBid = amount;
            lot.CurrentBidFranchiseId = fid;
            lot.LastBidAt = now;
            db.AuctionBids.Add(new AuctionBid { AuctionPlayerId = lot.Id, FranchiseId = fid, Amount = amount, PlacedBy = me.UserId, PlacedAt = now, IdempotencyKey = req.IdempotencyKey });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        audit.Record("auctions.bid", "AuctionPlayer", req.LotId, new { amount }, fid);
        await db.SaveChangesAsync(ct);
        await BroadcastAsync(auctionId, "bid", new { LotId = req.LotId, FranchiseId = fid, Amount = amount });
        return await LiveAsync(auctionId, ct);
    }

    /// <summary>Hammer: sells the lot to the highest bidder, booking purse, squad, contract and history in one transaction.</summary>
    public async Task<LiveAuctionDto> CloseLotAsync(Guid auctionId, bool sold, CancellationToken ct)
    {
        me.Require(Perm.AuctionsManage);
        Guid playerId = default, buyer = default;
        decimal price = 0;
        string playerName = "";
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var a = await db.AuctionEvents.FirstOrDefaultAsync(x => x.Id == auctionId, ct) ?? throw new NotFoundException("Auction");
            if (a.Status is not (AuctionStatus.Live or AuctionStatus.Paused)) throw new ConflictException("The auction is not running.");
            var lot = await db.AuctionPlayers.FromSqlInterpolated($"SELECT * FROM [ipl].[AuctionPlayers] WITH (UPDLOCK, ROWLOCK) WHERE [AuctionEventId] = {auctionId} AND [Status] = 'OnBlock'")
                .FirstOrDefaultAsync(ct) ?? throw new ConflictException("No lot is on the block.");
            var player = await db.Players.FirstAsync(p => p.Id == lot.PlayerId, ct);
            var now = clock.UtcNow;
            playerId = player.Id;
            playerName = player.FullName;
            if (sold)
            {
                if (lot.CurrentBid is null || lot.CurrentBidFranchiseId is null) throw new ConflictException("There are no bids: mark the player unsold instead.");
                buyer = lot.CurrentBidFranchiseId.Value;
                price = lot.CurrentBid.Value;
                // Re-check the rules at the hammer (purse/squad could have changed since the bid).
                var squad = await SquadMath.LoadAsync(db, buyer, a.SeasonId, clock.Today, ct);
                var seasonRules = await rules.ForSeasonAsync(a.SeasonId, ct);
                if (squad.Count >= seasonRules.Int(RuleKeys.MaxSquadSize)) throw new DomainException("auction.squad_full", "The winning franchise's squad is full.");
                if (player.IsOverseas && squad.Count(m => m.IsOverseas) >= seasonRules.Int(RuleKeys.MaxOverseasInSquad))
                    throw new DomainException("auction.overseas_full", "The winning franchise has no overseas slots left.");
                await Ledger.PostAsync(db, buyer, a.SeasonId, FinanceCategory.AuctionPurse, FinanceDirection.Debit, price,
                    $"Auction purchase: {player.FullName}", "AuctionPlayer", lot.Id.ToString(), $"auction:{lot.Id}:round{lot.Round}", me.UserId, now, ct);
                lot.Status = AuctionLotStatus.Sold;
                db.FranchiseSquads.Add(new FranchiseSquad { FranchiseId = buyer, SeasonId = a.SeasonId, PlayerId = player.Id, Acquisition = AcquisitionType.Auction, Price = price, Status = SquadMemberStatus.Active, JoinedAt = now });
                player.CurrentFranchiseId = buyer;
                player.Status = PlayerStatus.UnderContract;
                var season = await db.Seasons.FirstAsync(s => s.Id == a.SeasonId, ct);
                db.PlayerContracts.Add(new PlayerContract
                {
                    PlayerId = player.Id, FranchiseId = buyer, StartSeasonId = a.SeasonId, EndSeasonId = a.SeasonId, StartDate = season.StartDate,
                    EndDate = season.EndDate, AnnualSalary = price, Acquisition = AcquisitionType.Auction, Status = ContractStatus.Active,
                    Notes = $"Bought at {a.Name}",
                });
                db.PlayerTeams.Add(new PlayerTeam { PlayerId = player.Id, FranchiseId = buyer, SeasonId = a.SeasonId, Acquisition = AcquisitionType.Auction, Price = price, FromDate = clock.Today });
            }
            else
            {
                lot.Status = AuctionLotStatus.Unsold;
            }
            lot.ClosedAt = now;
            a.CurrentLotId = null;
            db.AuctionResults.Add(new AuctionResult
            {
                AuctionEventId = auctionId, AuctionPlayerId = lot.Id, PlayerId = player.Id, FranchiseId = sold ? buyer : null,
                Outcome = sold ? AuctionOutcome.Sold : AuctionOutcome.Unsold, Price = sold ? price : null, Round = lot.Round, At = now,
            });
            audit.Record(sold ? "auctions.sold" : "auctions.unsold", "AuctionPlayer", lot.Id, new { player.FullName, price, buyer }, sold ? buyer : null);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        await BroadcastAsync(auctionId, sold ? "sold" : "unsold", new { PlayerId = playerId, FranchiseId = sold ? buyer : (Guid?)null, Price = sold ? price : (decimal?)null, Player = playerName });
        if (sold)
        {
            await notify.NotifyFranchiseAsync(buyer, "auction.won", $"You bought {playerName}", $"Hammer price {Money.Format(price)}.", $"/squad", [Perm.SquadRead], ct: ct);
            await db.SaveChangesAsync(ct);
        }
        return await LiveAsync(auctionId, ct);
    }

    /// <summary>Puts unsold players back in the pool for another round (if the auction allows it).</summary>
    public async Task<int> ReopenUnsoldAsync(Guid auctionId, IReadOnlyList<Guid>? lotIds, CancellationToken ct)
    {
        me.Require(Perm.AuctionsManage);
        var a = await db.AuctionEvents.FirstOrDefaultAsync(x => x.Id == auctionId, ct) ?? throw new NotFoundException("Auction");
        if (!a.AllowReopenUnsold) throw new ConflictException("This auction does not allow unsold players to be reopened.");
        if (a.Status is AuctionStatus.Completed) throw new ConflictException("The auction is complete.");
        var lots = await db.AuctionPlayers.Where(l => l.AuctionEventId == auctionId && l.Status == AuctionLotStatus.Unsold && (lotIds == null || lotIds.Contains(l.Id))).ToListAsync(ct);
        a.CurrentRound++;
        foreach (var l in lots)
        {
            l.Status = AuctionLotStatus.Pending;
            l.Round = a.CurrentRound;
            l.CurrentBid = null;
            l.CurrentBidFranchiseId = null;
        }
        audit.Record("auctions.reopened_unsold", "AuctionEvent", auctionId, new { count = lots.Count, a.CurrentRound });
        await SaveAsync(ct);
        await BroadcastAsync(auctionId, "status", new { Status = a.Status.ToString() });
        return lots.Count;
    }

    // ---------------- Live view & results ----------------

    public async Task<LiveAuctionDto> LiveAsync(Guid auctionId, CancellationToken ct)
    {
        me.Require(Perm.AuctionsRead);
        var a = await db.AuctionEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == auctionId, ct) ?? throw new NotFoundException("Auction");
        var current = await LotQuery(auctionId, AuctionLotStatus.OnBlock).FirstOrDefaultAsync(ct);
        var seasonRules = await rules.ForSeasonAsync(a.SeasonId, ct);
        decimal? next = current is null ? null : BidRules.NextBid(current.BasePrice, current.CurrentBid, seasonRules.BidIncrements());
        var bids = current is null ? [] : await db.AuctionBids.AsNoTracking().Where(b => b.AuctionPlayerId == current.Id).OrderByDescending(b => b.PlacedAt).Take(30)
            .Select(b => new BidDto(b.Id, b.FranchiseId, b.Franchise!.ShortName, b.Amount, b.PlacedAt, null)).ToListAsync(ct);
        var franchises = await PursesAsync(a, seasonRules, ct);
        var counts = await db.AuctionPlayers.AsNoTracking().Where(l => l.AuctionEventId == auctionId).GroupBy(l => l.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        MyAuctionStateDto? mine = null;
        if (me.FranchiseId is { } fid && me.Has(Perm.AuctionsBid))
        {
            string? reason;
            if (current is null) reason = "No player is on the block.";
            else
            {
                var lot = await db.AuctionPlayers.AsNoTracking().FirstAsync(l => l.Id == current.Id, ct);
                reason = (await CheckBidAsync(a, lot, fid, ct)).Reason;
            }
            mine = new MyAuctionStateDto(fid, reason is null, reason, next);
        }
        return new LiveAuctionDto(a.Id, a.Name, a.Status.ToString(), a.CurrentRound, a.BidTimerSeconds, a.AllowReopenUnsold, current, next, bids, franchises,
            counts.GetValueOrDefault(AuctionLotStatus.Pending), counts.GetValueOrDefault(AuctionLotStatus.Sold), counts.GetValueOrDefault(AuctionLotStatus.Unsold),
            clock.UtcNow, mine);
    }

    public async Task<AuctionResultsDto> ResultsAsync(Guid auctionId, CancellationToken ct)
    {
        me.Require(Perm.AuctionsRead);
        var a = await db.AuctionEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == auctionId, ct) ?? throw new NotFoundException("Auction");
        var lots = await db.AuctionPlayers.AsNoTracking().Where(l => l.AuctionEventId == auctionId)
            .Select(l => new { l.Id, l.PlayerId, l.Player!.FullName, Role = l.Player.PrimaryRole!.Name, l.Player.IsOverseas, l.Status, l.BasePrice, l.Round })
            .ToListAsync(ct);
        var results = await db.AuctionResults.AsNoTracking().Where(r => r.AuctionEventId == auctionId)
            .Select(r => new { r.AuctionPlayerId, r.Outcome, r.FranchiseId, Franchise = r.Franchise != null ? r.Franchise.Name : null, r.Price, r.Round, r.At })
            .ToListAsync(ct);
        var latest = results.GroupBy(r => r.AuctionPlayerId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.At).First());
        var rows = lots.Where(l => latest.ContainsKey(l.Id)).Select(l =>
        {
            var r = latest[l.Id];
            return new ResultRowDto(l.PlayerId, l.FullName, l.Role, l.IsOverseas, r.Outcome.ToString(), r.Franchise, r.Price, l.BasePrice, r.Round);
        }).ToList();
        var sold = rows.Where(r => r.Outcome == nameof(AuctionOutcome.Sold)).OrderByDescending(r => r.Price).ToList();
        var unsold = rows.Where(r => r.Outcome == nameof(AuctionOutcome.Unsold) && lots.First(l => l.PlayerId == r.PlayerId).Status == AuctionLotStatus.Unsold).ToList();
        var purses = await db.FranchiseSeasons.AsNoTracking().Where(s => s.SeasonId == a.SeasonId).ToDictionaryAsync(s => s.FranchiseId, s => s.PurseRemaining, ct);
        var franchises = await db.Franchises.AsNoTracking().Where(f => f.IsActive).OrderBy(f => f.Name).ToListAsync(ct);
        var byFranchise = franchises.Select(f =>
        {
            var bought = sold.Where(s => s.Franchise == f.Name).ToList();
            return new FranchiseResultDto(f.Id, f.Name, bought.Count, bought.Sum(b => b.Price ?? 0), purses.GetValueOrDefault(f.Id),
                bought.Count == 0 ? null : Math.Round(bought.Average(b => b.Price ?? 0), 0), bought);
        }).ToList();
        return new AuctionResultsDto(a.Id, a.Name, a.Status.ToString(), sold.Count, unsold.Count, sold.Sum(s => s.Price ?? 0),
            sold.Count == 0 ? null : Math.Round(sold.Average(s => s.Price ?? 0), 0), sold.FirstOrDefault(), sold, unsold, byFranchise);
    }

    // ---------------- Helpers ----------------

    private sealed record BidCheck(string? Reason, decimal? NextBid);

    private async Task<BidCheck> CheckBidAsync(AuctionEvent a, AuctionPlayer lot, Guid fid, CancellationToken ct)
    {
        var seasonRules = await rules.ForSeasonAsync(a.SeasonId, ct);
        if (a.Status != AuctionStatus.Live) return new("The auction is not live.", null);
        if (lot.Status != AuctionLotStatus.OnBlock) return new("This player is not on the block.", null);
        var next = BidRules.NextBid(lot.BasePrice, lot.CurrentBid, seasonRules.BidIncrements());
        if (lot.CurrentBidFranchiseId == fid) return new("You already hold the highest bid.", next);
        var player = await db.Players.AsNoTracking().FirstAsync(p => p.Id == lot.PlayerId, ct);
        if (player.CurrentFranchiseId is not null) return new("This player is no longer eligible (already signed).", next);
        var squad = await SquadMath.LoadAsync(db, fid, a.SeasonId, clock.Today, ct);
        var max = seasonRules.Int(RuleKeys.MaxSquadSize);
        if (squad.Count >= max) return new($"Your squad is full ({max} players).", next);
        if (player.IsOverseas && squad.Count(m => m.IsOverseas) >= seasonRules.Int(RuleKeys.MaxOverseasInSquad))
            return new("You have no overseas slots left.", next);
        var purse = await db.FranchiseSeasons.AsNoTracking().Where(s => s.FranchiseId == fid && s.SeasonId == a.SeasonId).Select(s => (decimal?)s.PurseRemaining).FirstOrDefaultAsync(ct)
            ?? 0;
        var reserve = MinimumSquadReserve(seasonRules, squad.Count + 1);
        if (next > purse - reserve)
            return new(reserve > 0
                ? $"Not enough purse: {Money.Format(purse)} left and {Money.Format(reserve)} must be kept to complete the minimum squad."
                : $"Not enough purse: {Money.Format(purse)} left.", next);
        return new(null, next);
    }

    private static decimal MinimumSquadReserve(SeasonRules r, int squadAfter)
    {
        if (!r.BoolOr(RuleKeys.ReserveForMinimumSquad, false)) return 0;
        var stillNeeded = Math.Max(0, r.Int(RuleKeys.MinSquadSize) - squadAfter);
        return stillNeeded * r.Decimal(RuleKeys.MinBasePrice);
    }

    private async Task<List<FranchisePurseDto>> PursesAsync(AuctionEvent a, SeasonRules seasonRules, CancellationToken ct)
    {
        var franchises = await db.Franchises.AsNoTracking().Where(f => f.IsActive).OrderBy(f => f.Name).ToListAsync(ct);
        var purses = await db.FranchiseSeasons.AsNoTracking().Where(s => s.SeasonId == a.SeasonId).ToDictionaryAsync(s => s.FranchiseId, s => s.PurseRemaining, ct);
        var squads = await db.FranchiseSquads.AsNoTracking().Where(s => s.SeasonId == a.SeasonId && s.Status == SquadMemberStatus.Active)
            .GroupBy(s => s.FranchiseId).Select(g => new { g.Key, Count = g.Count(), Overseas = g.Count(x => x.Player!.IsOverseas) }).ToDictionaryAsync(x => x.Key, ct);
        var bought = await db.AuctionResults.AsNoTracking().Where(r => r.AuctionEventId == a.Id && r.Outcome == AuctionOutcome.Sold && r.FranchiseId != null)
            .GroupBy(r => r.FranchiseId!.Value).Select(g => new { g.Key, Count = g.Count(), Spent = g.Sum(r => r.Price ?? 0) }).ToDictionaryAsync(x => x.Key, ct);
        var max = seasonRules.Int(RuleKeys.MaxSquadSize);
        var maxOs = seasonRules.Int(RuleKeys.MaxOverseasInSquad);
        return franchises.Select(f =>
        {
            var sq = squads.GetValueOrDefault(f.Id);
            var size = sq?.Count ?? 0;
            var purse = purses.GetValueOrDefault(f.Id);
            var maxBid = Math.Max(0, purse - MinimumSquadReserve(seasonRules, size + 1));
            return new FranchisePurseDto(f.Id, f.Name, f.ShortName, f.PrimaryColor, purse, size, sq?.Overseas ?? 0, Math.Max(0, max - size),
                Math.Max(0, maxOs - (sq?.Overseas ?? 0)), bought.GetValueOrDefault(f.Id)?.Count ?? 0, bought.GetValueOrDefault(f.Id)?.Spent ?? 0, maxBid);
        }).ToList();
    }

    private IQueryable<LotDto> LotQuery(Guid auctionId, AuctionLotStatus? status = null)
    {
        var today = clock.Today;
        return db.AuctionPlayers.AsNoTracking().Where(l => l.AuctionEventId == auctionId && (status == null || l.Status == status))
            .OrderBy(l => l.Round).ThenBy(l => l.SetNumber).ThenBy(l => l.LotNumber)
            .Select(l => new LotDto(l.Id, l.PlayerId, l.Player!.FullName, l.Player.PrimaryRole!.Name, l.Player.Country!.Name, l.Player.IsOverseas,
                l.Player.IsCapped, l.Player.DateOfBirth == null ? null : today.Year - l.Player.DateOfBirth.Value.Year, l.SetName, l.SetNumber, l.LotNumber, l.BasePrice, l.Status.ToString(), l.CurrentBid,
                l.CurrentBidFranchiseId, db.Franchises.Where(f => f.Id == l.CurrentBidFranchiseId).Select(f => f.ShortName).FirstOrDefault(),
                l.LastBidAt, l.Round, l.Player.PhotoUrl));
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("The auction changed at the same time. Try again."); }
    }

    private Task BroadcastAsync(Guid auctionId, string kind, object payload) =>
        realtime.ToAuction(auctionId, "auction", new { AuctionId = auctionId, Kind = kind, Payload = payload, At = clock.UtcNow });
}
