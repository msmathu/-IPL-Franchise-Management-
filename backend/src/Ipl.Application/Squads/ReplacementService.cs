using System.Data;
using Ipl.Application.Finance;
using Ipl.Application.Rules;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Squads;

public record ReplacementRequestDto(
    Guid Id, Guid FranchiseId, string FranchiseName, string FranchiseCode, string Season,
    Guid InjuredPlayerId, string InjuredPlayerName, decimal? InjuredPlayerFee,
    Guid ReplacementPlayerId, string ReplacementPlayerName, bool ReplacementIsOverseas,
    decimal Fee, string? Reason, string Status, DateTime RequestedAt, string? DecisionNote, DateTime? DecidedAt);

public record CreateReplacementRequest(Guid InjuredPlayerId, Guid ReplacementPlayerId, decimal Fee, string? Reason);

public record ReplacementDecision(string? Note);

public record ReplacementRulesDto(bool Enabled, bool FeeCappedAtInjuredFee, bool FeeAffectsPurse);

/// <summary>
/// Injury replacements: a franchise asks to replace an injured squad player with an unsigned player; the league approves
/// or rejects. Approval swaps the two players in one transaction. Whether replacements are allowed, whether the fee is
/// capped at the injured player's fee and whether it is charged to the auction purse are season rules.
/// </summary>
public class ReplacementService(IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, IAuditService audit, INotificationService notify)
{
    public async Task<ReplacementRulesDto> RulesAsync(CancellationToken ct)
    {
        me.Require(Perm.SquadRead);
        var season = await rules.CurrentSeasonAsync(ct);
        var r = await rules.ForSeasonAsync(season.Id, ct);
        return new ReplacementRulesDto(r.BoolOr(RuleKeys.ReplacementsEnabled, true), r.BoolOr(RuleKeys.ReplacementFeeCap, true), r.BoolOr(RuleKeys.ReplacementAffectsPurse, false));
    }

    public async Task<IReadOnlyList<ReplacementRequestDto>> ListAsync(CancellationToken ct)
    {
        me.Require(Perm.SquadRead);
        var q = db.ReplacementRequests.AsNoTracking().AsQueryable();
        if (!me.IsLeagueWide) q = q.Where(r => r.FranchiseId == me.RequireFranchise());
        var rows = await q.OrderByDescending(r => r.RequestedAt).Take(200)
            .Select(r => new
            {
                R = r, Franchise = r.Franchise!.Name, r.Franchise.Code, Season = db.Seasons.Where(s => s.Id == r.SeasonId).Select(s => s.Name).First(),
                Injured = r.InjuredPlayer!.FullName, Replacement = r.ReplacementPlayer!.FullName, r.ReplacementPlayer.IsOverseas,
                InjuredFee = db.FranchiseSquads.Where(s => s.FranchiseId == r.FranchiseId && s.SeasonId == r.SeasonId && s.PlayerId == r.InjuredPlayerId)
                    .Select(s => (decimal?)s.Price).FirstOrDefault(),
            }).ToListAsync(ct);
        return rows.Select(x => new ReplacementRequestDto(x.R.Id, x.R.FranchiseId, x.Franchise, x.Code, x.Season, x.R.InjuredPlayerId, x.Injured,
            x.InjuredFee > 0 ? x.InjuredFee : null, x.R.ReplacementPlayerId, x.Replacement, x.IsOverseas, x.R.Fee, x.R.Reason, x.R.Status.ToString(),
            x.R.RequestedAt, x.R.DecisionNote, x.R.DecidedAt)).ToList();
    }

    public async Task<Guid> CreateAsync(CreateReplacementRequest req, CancellationToken ct)
    {
        me.Require(Perm.SquadManage);
        var fid = me.RequireFranchise();
        var season = await rules.CurrentSeasonAsync(ct);
        await ValidateAsync(fid, season.Id, req.InjuredPlayerId, req.ReplacementPlayerId, req.Fee, null, ct);
        if (await db.ReplacementRequests.AnyAsync(r => r.InjuredPlayerId == req.InjuredPlayerId && r.SeasonId == season.Id && r.Status == ReplacementStatus.Pending, ct))
            throw new ConflictException("There is already a pending replacement request for this player.");
        var row = new ReplacementRequest
        {
            FranchiseId = fid, SeasonId = season.Id, InjuredPlayerId = req.InjuredPlayerId, ReplacementPlayerId = req.ReplacementPlayerId,
            Fee = req.Fee, Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim(), Status = ReplacementStatus.Pending,
            RequestedAt = clock.UtcNow, RequestedBy = me.UserId,
        };
        db.ReplacementRequests.Add(row);
        audit.Record("replacement.requested", "ReplacementRequest", row.Id, new { req.InjuredPlayerId, req.ReplacementPlayerId, req.Fee }, fid);
        await db.SaveChangesAsync(ct);
        var names = await PlayerNamesAsync(req.InjuredPlayerId, req.ReplacementPlayerId, ct);
        await notify.NotifyLeagueAsync(Perm.TradesLeagueApprove, "replacement.requested", "Injury replacement awaiting approval",
            $"{names.Replacement} to replace {names.Injured}.", "/replacements", ct);
        return row.Id;
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        me.Require(Perm.SquadManage);
        var r = await db.ReplacementRequests.FirstOrDefaultAsync(x => x.Id == id && x.FranchiseId == me.RequireFranchise(), ct) ?? throw new NotFoundException("Replacement request");
        if (r.Status != ReplacementStatus.Pending) throw new ConflictException("Only pending requests can be cancelled.");
        r.Status = ReplacementStatus.Cancelled;
        r.DecidedAt = clock.UtcNow;
        r.DecidedBy = me.UserId;
        audit.Record("replacement.cancelled", "ReplacementRequest", id, null, r.FranchiseId);
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid id, string? note, CancellationToken ct)
    {
        me.Require(Perm.TradesLeagueApprove);
        var r = await db.ReplacementRequests.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Replacement request");
        if (r.Status != ReplacementStatus.Pending) throw new ConflictException("This request has already been decided.");
        if (string.IsNullOrWhiteSpace(note)) throw new AppValidationException("note", "Give the franchise a reason for the rejection.");
        r.Status = ReplacementStatus.Rejected;
        r.DecisionNote = note.Trim();
        r.DecidedAt = clock.UtcNow;
        r.DecidedBy = me.UserId;
        audit.Record("replacement.rejected", "ReplacementRequest", id, new { note }, r.FranchiseId);
        await db.SaveChangesAsync(ct);
        await notify.NotifyFranchiseAsync(r.FranchiseId, "replacement.rejected", "Replacement rejected", r.DecisionNote, "/replacements", [Perm.SquadManage], null, ct);
    }

    /// <summary>League approval: the injured player leaves the squad and the replacement joins, in one serializable transaction.</summary>
    public async Task ApproveAsync(Guid id, string? note, CancellationToken ct)
    {
        me.Require(Perm.TradesLeagueApprove);
        var now = clock.UtcNow;
        ReplacementRequest? r = null;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            r = await db.ReplacementRequests.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Replacement request");
            if (r.Status != ReplacementStatus.Pending) throw new ConflictException("This request has already been decided.");
            var seasonRules = await rules.ForSeasonAsync(r.SeasonId, ct);
            await ValidateAsync(r.FranchiseId, r.SeasonId, r.InjuredPlayerId, r.ReplacementPlayerId, r.Fee, seasonRules, ct);

            var outRow = await db.FranchiseSquads.IgnoreQueryFilters().FirstAsync(s => s.FranchiseId == r.FranchiseId && s.SeasonId == r.SeasonId
                && s.PlayerId == r.InjuredPlayerId && s.Status == SquadMemberStatus.Active, ct);
            outRow.Status = SquadMemberStatus.Released;
            outRow.LeftAt = now;
            var injured = await db.Players.FirstAsync(p => p.Id == r.InjuredPlayerId, ct);
            injured.CurrentFranchiseId = null; // stays "Injured"; can be signed again once fit

            var incoming = await db.Players.FirstAsync(p => p.Id == r.ReplacementPlayerId, ct);
            incoming.CurrentFranchiseId = r.FranchiseId;
            incoming.Status = PlayerStatus.UnderContract;
            db.FranchiseSquads.Add(new FranchiseSquad
            {
                FranchiseId = r.FranchiseId, SeasonId = r.SeasonId, PlayerId = incoming.Id, Acquisition = AcquisitionType.Replacement,
                Price = r.Fee, Status = SquadMemberStatus.Active, JoinedAt = now,
            });
            db.PlayerTeams.Add(new PlayerTeam
            {
                PlayerId = incoming.Id, FranchiseId = r.FranchiseId, SeasonId = r.SeasonId, Acquisition = AcquisitionType.Replacement,
                Price = r.Fee, FromDate = DateOnly.FromDateTime(now),
            });
            if (seasonRules.BoolOr(RuleKeys.ReplacementAffectsPurse, false) && r.Fee > 0)
                await Ledger.PostAsync(db, r.FranchiseId, r.SeasonId, FinanceCategory.AuctionPurse, FinanceDirection.Debit, r.Fee,
                    $"Injury replacement: {incoming.FullName} for {injured.FullName}", "ReplacementRequest", r.Id.ToString(), $"replacement:{r.Id}", me.UserId, now, ct);

            r.Status = ReplacementStatus.Approved;
            r.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            r.DecidedAt = now;
            r.DecidedBy = me.UserId;
            audit.Record("replacement.approved", "ReplacementRequest", r.Id, new { r.InjuredPlayerId, r.ReplacementPlayerId, r.Fee }, r.FranchiseId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        Analytics.RatingService.Invalidate();
        var names = await PlayerNamesAsync(r!.InjuredPlayerId, r.ReplacementPlayerId, ct);
        await notify.NotifyFranchiseAsync(r.FranchiseId, "replacement.approved", "Replacement approved",
            $"{names.Replacement} joins the squad in place of {names.Injured}.", "/squad", [Perm.SquadRead], null, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task ValidateAsync(Guid fid, int seasonId, Guid injuredId, Guid replacementId, decimal fee, SeasonRules? seasonRules, CancellationToken ct)
    {
        seasonRules ??= await rules.ForSeasonAsync(seasonId, ct);
        if (!seasonRules.BoolOr(RuleKeys.ReplacementsEnabled, true)) throw new ConflictException("Injury replacements are not allowed this season.");
        if (fee < 0) throw new AppValidationException("fee", "The fee cannot be negative.");
        if (injuredId == replacementId) throw new AppValidationException("replacementPlayerId", "Choose a different player as the replacement.");

        var outRow = await db.FranchiseSquads.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.FranchiseId == fid && s.SeasonId == seasonId
            && s.PlayerId == injuredId && s.Status == SquadMemberStatus.Active, ct)
            ?? throw new AppValidationException("injuredPlayerId", "That player is not in the squad.");
        var injuredStatus = await db.PlayerAvailability.Where(a => a.PlayerId == injuredId).Select(a => (AvailabilityStatus?)a.Status).FirstOrDefaultAsync(ct);
        if (injuredStatus != AvailabilityStatus.Injured)
            throw new AppValidationException("injuredPlayerId", "Only a player marked Injured (Medical & Availability) can be replaced.");

        var incoming = await db.Players.FirstOrDefaultAsync(p => p.Id == replacementId, ct) ?? throw new NotFoundException("Replacement player");
        if (incoming.CurrentFranchiseId is not null) throw new AppValidationException("replacementPlayerId", "The replacement must be an unsigned player.");
        if (incoming.Status is PlayerStatus.Retired or PlayerStatus.Injured or PlayerStatus.Unavailable)
            throw new AppValidationException("replacementPlayerId", "The replacement player is not available.");

        if (seasonRules.BoolOr(RuleKeys.ReplacementFeeCap, true) && outRow.Price > 0 && fee > outRow.Price)
            throw new AppValidationException("fee", $"The fee cannot exceed the injured player's fee ({Money.Format(outRow.Price)}).");

        var injured = await db.Players.FirstAsync(p => p.Id == injuredId, ct);
        if (incoming.IsOverseas && !injured.IsOverseas)
        {
            var overseas = await db.FranchiseSquads.IgnoreQueryFilters().CountAsync(s => s.FranchiseId == fid && s.SeasonId == seasonId
                && s.Status == SquadMemberStatus.Active && s.Player!.IsOverseas, ct);
            if (overseas >= seasonRules.Int(RuleKeys.MaxOverseasInSquad))
                throw new AppValidationException("replacementPlayerId", "No overseas slot is free for an overseas replacement.");
        }
    }

    private async Task<(string Injured, string Replacement)> PlayerNamesAsync(Guid injuredId, Guid replacementId, CancellationToken ct)
    {
        var names = await db.Players.Where(p => p.Id == injuredId || p.Id == replacementId).ToDictionaryAsync(p => p.Id, p => p.FullName, ct);
        return (names.GetValueOrDefault(injuredId, "player"), names.GetValueOrDefault(replacementId, "player"));
    }
}
