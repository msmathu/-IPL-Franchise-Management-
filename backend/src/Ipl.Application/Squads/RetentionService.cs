using System.Data;
using Ipl.Application.Finance;
using Ipl.Application.Rules;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Squads;

public record RetentionRulesDto(
    DateOnly? Deadline, int MaxTotal, int MaxCapped, int MaxUncapped, int MaxOverseas, IReadOnlyList<decimal> CappedSlotCosts,
    decimal UncappedCost, decimal Purse, int MaxSquad, int MaxOverseasSquad);

public record RetentionCandidateDto(
    Guid PlayerId, string Name, string Role, bool IsCapped, bool IsOverseas, int? Age, decimal CurrentSalary, string Availability, bool IsRetained, bool IsReleased);

public record RetentionSlotDto(Guid PlayerId, string Name, string Category, int SlotNumber, decimal Cost, bool IsOverseas);

public record RetentionSimulationDto(
    IReadOnlyList<RetentionSlotDto> Slots, decimal TotalCost, decimal PurseAfter, int SquadSlotsRemaining, int OverseasSlotsRemaining,
    IReadOnlyList<string> Violations, IReadOnlyList<string> Warnings, bool IsValid);

public record RetentionPlanDto(Guid Id, string Name, bool IsSubmitted, DateTime? SubmittedAt, DateTime CreatedAt, RetentionSimulationDto Simulation);

public record ReleaseDto(Guid Id, Guid PlayerId, string Name, string? Reason, DateTime ReleasedAt, int Year);

public record RetentionOverviewDto(
    int SeasonId, string Season, Guid FranchiseId, RetentionRulesDto Rules, bool IsBeforeDeadline, bool HasSubmitted,
    IReadOnlyList<RetentionCandidateDto> Squad, IReadOnlyList<RetentionPlanDto> Plans, IReadOnlyList<ReleaseDto> Releases);

public record RetentionHistoryDto(int Year, string Player, string Category, int Slot, decimal Cost, string Status);

public record SavePlanRequest(string Name, IReadOnlyList<Guid> PlayerIds);

public class RetentionService(IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, IAuditService audit, INotificationService notify)
{
    public async Task<RetentionOverviewDto> OverviewAsync(Guid? franchiseId, CancellationToken ct)
    {
        me.Require(Perm.RetentionRead);
        var fid = me.RequireFranchise(franchiseId);
        var season = await rules.CurrentSeasonAsync(ct);
        var r = await RulesAsync(season.Id, ct);
        var squad = await SquadMath.LoadAsync(db, fid, season.Id, clock.Today, ct);
        var retained = await db.RetentionRecords.AsNoTracking().Where(x => x.FranchiseId == fid && x.SeasonId == season.Id && x.Status == RetentionStatus.Confirmed)
            .Select(x => x.PlayerId).ToListAsync(ct);
        var releases = await db.ReleaseRecords.AsNoTracking().Where(x => x.FranchiseId == fid && x.Status == ReleaseStatus.Released)
            .OrderByDescending(x => x.ReleasedAt)
            .Select(x => new ReleaseDto(x.Id, x.PlayerId, x.Player!.FullName, x.Reason, x.ReleasedAt, db.Seasons.Where(s => s.Id == x.SeasonId).Select(s => s.Year).First()))
            .ToListAsync(ct);
        var plans = await db.RetentionPlans.AsNoTracking().Include(p => p.Records).Where(p => p.FranchiseId == fid && p.SeasonId == season.Id)
            .OrderByDescending(p => p.IsSubmitted).ThenByDescending(p => p.CreatedAt).ToListAsync(ct);
        List<RetentionPlanDto> planDtos = [];
        foreach (var p in plans)
            planDtos.Add(new RetentionPlanDto(p.Id, p.Name, p.IsSubmitted, p.SubmittedAt, p.CreatedAt,
                Simulate(p.Records.OrderBy(x => x.SlotNumber).Select(x => x.PlayerId).ToList(), squad, r)));
        var releasedThisSeason = releases.Where(x => x.Year == season.Year).Select(x => x.PlayerId).ToHashSet();
        return new RetentionOverviewDto(season.Id, season.Name, fid, r, r.Deadline is null || clock.Today <= r.Deadline,
            plans.Any(p => p.IsSubmitted),
            squad.Select(m => new RetentionCandidateDto(m.PlayerId, m.Name, m.RoleName, m.IsCapped, m.IsOverseas, m.Age, m.Salary, m.Availability,
                retained.Contains(m.PlayerId), releasedThisSeason.Contains(m.PlayerId))).OrderByDescending(m => m.CurrentSalary).ToList(),
            planDtos, releases);
    }

    public async Task<RetentionSimulationDto> SimulateAsync(IReadOnlyList<Guid> playerIds, Guid? franchiseId, CancellationToken ct)
    {
        me.Require(Perm.RetentionRead);
        var fid = me.RequireFranchise(franchiseId);
        var season = await rules.CurrentSeasonAsync(ct);
        var squad = await SquadMath.LoadAsync(db, fid, season.Id, clock.Today, ct);
        return Simulate(playerIds, squad, await RulesAsync(season.Id, ct));
    }

    public async Task<Guid> SavePlanAsync(SavePlanRequest req, CancellationToken ct)
    {
        me.Require(Perm.RetentionManage);
        var fid = me.RequireFranchise();
        if (string.IsNullOrWhiteSpace(req.Name)) throw new AppValidationException("name", "Name the plan.");
        var season = await rules.CurrentSeasonAsync(ct);
        var squad = await SquadMath.LoadAsync(db, fid, season.Id, clock.Today, ct);
        var sim = Simulate(req.PlayerIds, squad, await RulesAsync(season.Id, ct));
        var plan = new RetentionPlan { FranchiseId = fid, SeasonId = season.Id, Name = req.Name.Trim() };
        foreach (var slot in sim.Slots)
            plan.Records.Add(new RetentionRecord
            {
                SeasonId = season.Id, FranchiseId = fid, PlayerId = slot.PlayerId, Category = Enum.Parse<RetentionCategory>(slot.Category),
                SlotNumber = slot.SlotNumber, Cost = slot.Cost, Status = RetentionStatus.Draft, CreatedAt = clock.UtcNow,
            });
        db.RetentionPlans.Add(plan);
        audit.Record("retention.plan_saved", "RetentionPlan", plan.Id, new { plan.Name, Players = req.PlayerIds.Count, sim.TotalCost }, fid);
        await db.SaveChangesAsync(ct);
        return plan.Id;
    }

    public async Task DeletePlanAsync(Guid planId, CancellationToken ct)
    {
        me.Require(Perm.RetentionManage);
        var plan = await db.RetentionPlans.FirstOrDefaultAsync(p => p.Id == planId, ct) ?? throw new NotFoundException("Plan");
        if (plan.IsSubmitted) throw new ConflictException("A submitted retention list cannot be deleted.");
        db.RetentionPlans.Remove(plan);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Final submission: confirms retentions, deducts their cost from the auction purse and releases every other squad
    /// player into the auction pool — all in one transaction, once per franchise and season, before the deadline.
    /// </summary>
    public async Task<RetentionSimulationDto> SubmitAsync(Guid planId, CancellationToken ct)
    {
        me.Require(Perm.RetentionSubmit);
        var fid = me.RequireFranchise();
        RetentionSimulationDto? result = null;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var plan = await db.RetentionPlans.Include(p => p.Records).FirstOrDefaultAsync(p => p.Id == planId && p.FranchiseId == fid, ct)
                ?? throw new NotFoundException("Plan");
            var season = await db.Seasons.FirstAsync(s => s.Id == plan.SeasonId, ct);
            var r = await RulesAsync(season.Id, ct);
            if (r.Deadline is { } deadline && clock.Today > deadline) throw new DomainException("retention.deadline", $"The retention deadline was {deadline:d MMM yyyy}.");
            if (await db.RetentionPlans.AnyAsync(p => p.FranchiseId == fid && p.SeasonId == season.Id && p.IsSubmitted, ct))
                throw new ConflictException("Retentions for this season have already been submitted.");

            var squad = await SquadMath.LoadAsync(db, fid, season.Id, clock.Today, ct);
            var sim = Simulate(plan.Records.OrderBy(x => x.SlotNumber).Select(x => x.PlayerId).ToList(), squad, r);
            if (!sim.IsValid) throw new DomainException("retention.invalid", string.Join(" ", sim.Violations));

            var now = clock.UtcNow;
            db.RetentionRecords.RemoveRange(plan.Records);
            plan.Records.Clear();
            foreach (var slot in sim.Slots)
            {
                plan.Records.Add(new RetentionRecord
                {
                    SeasonId = season.Id, FranchiseId = fid, PlayerId = slot.PlayerId, Category = Enum.Parse<RetentionCategory>(slot.Category),
                    SlotNumber = slot.SlotNumber, Cost = slot.Cost, Status = RetentionStatus.Confirmed, CreatedAt = now,
                });
                if (slot.Cost > 0)
                    await Ledger.PostAsync(db, fid, season.Id, FinanceCategory.AuctionPurse, FinanceDirection.Debit, slot.Cost,
                        $"Retention slot {slot.SlotNumber}: {slot.Name}", "RetentionPlan", plan.Id.ToString(), $"retention:{season.Id}:{slot.PlayerId}", me.UserId, now, ct);
                var squadRow = await db.FranchiseSquads.FirstAsync(s => s.FranchiseId == fid && s.SeasonId == season.Id && s.PlayerId == slot.PlayerId && s.Status == SquadMemberStatus.Active, ct);
                squadRow.Acquisition = AcquisitionType.Retained;
                squadRow.Price = slot.Cost;
                var player = await db.Players.FirstAsync(p => p.Id == slot.PlayerId, ct);
                player.Status = PlayerStatus.Retained;
            }
            var retainedIds = sim.Slots.Select(s => s.PlayerId).ToHashSet();
            foreach (var m in squad.Where(m => !retainedIds.Contains(m.PlayerId)))
                await ReleaseInternalAsync(fid, season.Id, m.PlayerId, "Not retained", now, ct);
            plan.IsSubmitted = true;
            plan.SubmittedAt = now;
            audit.Record("retention.submitted", "RetentionPlan", plan.Id, new { plan.Name, Retained = sim.Slots.Count, sim.TotalCost, Released = squad.Count - sim.Slots.Count }, fid);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            result = sim;
        });
        await notify.NotifyFranchiseAsync(fid, "retention.submitted", "Retention list submitted",
            $"{result!.Slots.Count} players retained for {Money.Format(result.TotalCost)}.", "/retention", [Perm.RetentionRead], ct: ct);
        await notify.NotifyLeagueAsync(Perm.AuctionsManage, "retention.submitted", "A franchise submitted retentions", $"{result.Slots.Count} players retained.", "/auction", ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task ReleaseAsync(Guid playerId, string? reason, CancellationToken ct)
    {
        me.Require(Perm.RetentionManage);
        var fid = me.RequireFranchise();
        var season = await rules.CurrentSeasonAsync(ct);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (await db.TradeOfferPlayers.AnyAsync(p => p.PlayerId == playerId && (p.TradeOffer!.Status == TradeStatus.Approved || p.TradeOffer.Status == TradeStatus.AwaitingLeagueApproval), ct))
                throw new ConflictException("This player is part of a trade awaiting completion.");
            await ReleaseInternalAsync(fid, season.Id, playerId, reason, clock.UtcNow, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    public async Task<IReadOnlyList<RetentionHistoryDto>> HistoryAsync(Guid? franchiseId, CancellationToken ct)
    {
        me.Require(Perm.RetentionRead);
        var fid = me.RequireFranchise(franchiseId);
        return await db.RetentionRecords.AsNoTracking().Where(r => r.FranchiseId == fid && r.Status == RetentionStatus.Confirmed)
            .OrderByDescending(r => r.SeasonId).ThenBy(r => r.SlotNumber)
            .Select(r => new RetentionHistoryDto(db.Seasons.Where(s => s.Id == r.SeasonId).Select(s => s.Year).First(), r.Player!.FullName,
                r.Category.ToString(), r.SlotNumber, r.Cost, r.Status.ToString()))
            .ToListAsync(ct);
    }

    private async Task ReleaseInternalAsync(Guid fid, int seasonId, Guid playerId, string? reason, DateTime now, CancellationToken ct)
    {
        var squadRow = await db.FranchiseSquads.FirstOrDefaultAsync(s => s.FranchiseId == fid && s.SeasonId == seasonId && s.PlayerId == playerId && s.Status == SquadMemberStatus.Active, ct)
            ?? throw new AppValidationException("playerId", "That player is not in your current squad.");
        squadRow.Status = SquadMemberStatus.Released;
        squadRow.LeftAt = now;
        var player = await db.Players.FirstAsync(p => p.Id == playerId, ct);
        player.CurrentFranchiseId = null;
        player.Status = PlayerStatus.AvailableForAuction;
        foreach (var c in await db.PlayerContracts.Where(c => c.PlayerId == playerId && c.FranchiseId == fid && c.Status == ContractStatus.Active).ToListAsync(ct))
        {
            c.Status = ContractStatus.Terminated;
            foreach (var pay in await db.ContractPayments.Where(p => p.PlayerContractId == c.Id && p.Status == PaymentStatus.Scheduled).ToListAsync(ct))
                pay.Status = PaymentStatus.Cancelled;
        }
        foreach (var t in await db.PlayerTeams.Where(t => t.PlayerId == playerId && t.FranchiseId == fid && t.ToDate == null).ToListAsync(ct))
            t.ToDate = DateOnly.FromDateTime(now);
        db.ReleaseRecords.Add(new ReleaseRecord { SeasonId = seasonId, FranchiseId = fid, PlayerId = playerId, Reason = reason, Status = ReleaseStatus.Released, ReleasedAt = now, ReleasedBy = me.UserId });
        audit.Record("retention.player_released", "Player", playerId, new { player.FullName, reason }, fid);
    }

    private async Task<RetentionRulesDto> RulesAsync(int seasonId, CancellationToken ct)
    {
        var r = await rules.ForSeasonAsync(seasonId, ct);
        return new RetentionRulesDto(r.DateOrNull(RuleKeys.RetentionDeadline), r.Int(RuleKeys.MaxRetentions), r.Int(RuleKeys.MaxCappedRetentions),
            r.Int(RuleKeys.MaxUncappedRetentions), r.Int(RuleKeys.MaxOverseasRetentions), r.Json<List<decimal>>(RuleKeys.CappedRetentionCosts),
            r.Decimal(RuleKeys.UncappedRetentionCost), r.Decimal(RuleKeys.AuctionPurse), r.Int(RuleKeys.MaxSquadSize), r.Int(RuleKeys.MaxOverseasInSquad));
    }

    /// <summary>Pure rule check of a retention list (order = capped slot order). Unit tested.</summary>
    public static RetentionSimulationDto Simulate(IReadOnlyList<Guid> playerIds, IReadOnlyList<SquadMember> squad, RetentionRulesDto r)
    {
        List<string> violations = [];
        List<string> warnings = [];
        List<RetentionSlotDto> slots = [];
        int capped = 0, uncapped = 0;
        foreach (var id in playerIds.Distinct())
        {
            var m = squad.FirstOrDefault(s => s.PlayerId == id);
            if (m is null) { violations.Add("A selected player is not in the current squad."); continue; }
            if (m.IsCapped)
            {
                capped++;
                var cost = capped <= r.CappedSlotCosts.Count ? r.CappedSlotCosts[capped - 1] : 0;
                if (capped > r.CappedSlotCosts.Count) violations.Add($"No cost is configured for capped retention #{capped}.");
                slots.Add(new RetentionSlotDto(m.PlayerId, m.Name, nameof(RetentionCategory.Capped), slots.Count + 1, cost, m.IsOverseas));
            }
            else
            {
                uncapped++;
                slots.Add(new RetentionSlotDto(m.PlayerId, m.Name, nameof(RetentionCategory.Uncapped), slots.Count + 1, r.UncappedCost, m.IsOverseas));
            }
            if (m.Availability != nameof(AvailabilityStatus.Available)) warnings.Add($"{m.Name} is currently {m.Availability}.");
            if (m.Age >= 35) warnings.Add($"{m.Name} is {m.Age}.");
        }
        if (slots.Count > r.MaxTotal) violations.Add($"{slots.Count} retentions selected; the maximum is {r.MaxTotal}.");
        if (capped > r.MaxCapped) violations.Add($"{capped} capped players selected; the maximum is {r.MaxCapped}.");
        if (uncapped > r.MaxUncapped) violations.Add($"{uncapped} uncapped players selected; the maximum is {r.MaxUncapped}.");
        var overseas = slots.Count(s => s.IsOverseas);
        if (overseas > r.MaxOverseas) violations.Add($"{overseas} overseas players selected; the maximum is {r.MaxOverseas}.");
        var total = slots.Sum(s => s.Cost);
        var purseAfter = r.Purse - total;
        if (purseAfter < 0) violations.Add("Retention costs exceed the auction purse.");
        return new RetentionSimulationDto(slots, total, purseAfter, r.MaxSquad - slots.Count, Math.Max(0, r.MaxOverseasSquad - overseas),
            violations.Distinct().ToList(), warnings, violations.Count == 0);
    }
}
