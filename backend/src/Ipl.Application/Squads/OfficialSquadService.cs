using System.Data;
using Ipl.Application.Finance;
using Ipl.Application.Rules;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Squads;

public record OfficialTransfer(Guid PlayerId, Guid ToFranchiseId, decimal Fee);

public record OfficialTeamList(Guid FranchiseId, IReadOnlyList<Guid> RetainedPlayerIds, decimal? PurseRemaining);

/// <summary>
/// The league's official squad announcement for the current season (e.g. the retention-day media advisory):
/// confirmed trades, each team's retained players and remaining purse. Everyone not retained is released.
/// </summary>
public record OfficialSquadUpdateRequest(string Source, decimal? AuctionPurse, IReadOnlyList<OfficialTransfer> Transfers, IReadOnlyList<OfficialTeamList> Teams);

public record OfficialSquadUpdateResult(int Transfers, int Released, int Retained, int PursesSet, IReadOnlyList<string> Notes);

/// <summary>
/// Records an official squad announcement in one serializable transaction, using the same records the app's own
/// workflows write: traded players get "Trade" squad rows and team history, released players get release records and
/// return to the auction pool, each team gets a confirmed retention list, and purses are set through the ledger.
/// </summary>
public class OfficialSquadService(IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, IAuditService audit)
{
    public async Task<OfficialSquadUpdateResult> ApplyAsync(OfficialSquadUpdateRequest req, CancellationToken ct)
    {
        me.Require(Perm.SeasonsManage);
        if (string.IsNullOrWhiteSpace(req.Source)) throw new AppValidationException("source", "Record where the announcement comes from.");
        var season = await rules.CurrentSeasonAsync(ct);
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now);
        List<string> notes = [];
        int transfers = 0, released = 0, retained = 0, purses = 0;

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            notes.Clear(); transfers = released = retained = purses = 0;
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var squads = await db.FranchiseSquads.IgnoreQueryFilters().Where(s => s.SeasonId == season.Id && s.Status == SquadMemberStatus.Active).ToListAsync(ct);
            var playerIds = squads.Select(s => s.PlayerId).Concat(req.Transfers.Select(t => t.PlayerId)).Concat(req.Teams.SelectMany(t => t.RetainedPlayerIds)).Distinct().ToList();
            var players = await db.Players.Where(p => playerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            var franchises = await db.Franchises.ToDictionaryAsync(f => f.Id, ct);

            // 1) Confirmed trades.
            foreach (var t in req.Transfers)
            {
                if (!players.TryGetValue(t.PlayerId, out var p)) throw new AppValidationException("transfers", $"Unknown player {t.PlayerId}.");
                if (!franchises.ContainsKey(t.ToFranchiseId)) throw new AppValidationException("transfers", $"Unknown franchise for {p.FullName}.");
                var from = squads.FirstOrDefault(s => s.PlayerId == p.Id);
                if (from?.FranchiseId == t.ToFranchiseId) { notes.Add($"{p.FullName} is already with {franchises[t.ToFranchiseId].Code}."); continue; }
                if (from is not null) { from.Status = SquadMemberStatus.TradedOut; from.LeftAt = now; squads.Remove(from); }
                var row = new FranchiseSquad
                {
                    FranchiseId = t.ToFranchiseId, SeasonId = season.Id, PlayerId = p.Id, Acquisition = AcquisitionType.Trade, Price = t.Fee,
                    Status = SquadMemberStatus.Active, JoinedAt = now,
                };
                db.FranchiseSquads.Add(row);
                squads.Add(row);
                db.PlayerTeams.Add(new PlayerTeam { PlayerId = p.Id, FranchiseId = t.ToFranchiseId, SeasonId = season.Id, Acquisition = AcquisitionType.Trade, Price = t.Fee, FromDate = today });
                p.CurrentFranchiseId = t.ToFranchiseId;
                p.Status = PlayerStatus.UnderContract;
                audit.Record("squad.official_transfer", "Player", p.Id,
                    new { player = p.FullName, from = from is null ? null : franchises[from.FranchiseId].Code, to = franchises[t.ToFranchiseId].Code, t.Fee, req.Source }, t.ToFranchiseId);
                transfers++;
            }

            // 2) Retained lists: everyone else in the squad is released.
            foreach (var team in req.Teams)
            {
                if (!franchises.TryGetValue(team.FranchiseId, out var f)) throw new AppValidationException("teams", "Unknown franchise.");
                var keep = team.RetainedPlayerIds.ToHashSet();
                foreach (var pid in keep)
                {
                    var p = players.GetValueOrDefault(pid) ?? throw new AppValidationException("teams", $"Unknown retained player {pid}.");
                    var row = squads.FirstOrDefault(s => s.PlayerId == pid);
                    if (row is null)
                    {
                        // Not in any squad here yet (e.g. a player missing from earlier data): join as retained.
                        row = new FranchiseSquad { FranchiseId = f.Id, SeasonId = season.Id, PlayerId = pid, Acquisition = AcquisitionType.Retained, Status = SquadMemberStatus.Active, JoinedAt = now };
                        db.FranchiseSquads.Add(row);
                        squads.Add(row);
                        db.PlayerTeams.Add(new PlayerTeam { PlayerId = pid, FranchiseId = f.Id, SeasonId = season.Id, Acquisition = AcquisitionType.Retained, FromDate = today });
                        notes.Add($"{p.FullName} added to {f.Code} (was not in a squad).");
                    }
                    else if (row.FranchiseId != f.Id)
                        throw new AppValidationException("teams", $"{p.FullName} is listed by {f.Code} but plays for {franchises[row.FranchiseId].Code} and no trade moves him.");
                    else if (row.Acquisition != AcquisitionType.Trade)
                        row.Acquisition = AcquisitionType.Retained;
                    p.CurrentFranchiseId = f.Id;
                    if (p.Status is not PlayerStatus.Injured) p.Status = row.Acquisition == AcquisitionType.Trade ? PlayerStatus.UnderContract : PlayerStatus.Retained;
                }
                foreach (var row in squads.Where(s => s.FranchiseId == f.Id && !keep.Contains(s.PlayerId)).ToList())
                {
                    row.Status = SquadMemberStatus.Released;
                    row.LeftAt = now;
                    squads.Remove(row);
                    var p = players[row.PlayerId];
                    p.CurrentFranchiseId = null;
                    if (p.Status is not PlayerStatus.Retired) p.Status = PlayerStatus.AvailableForAuction;
                    db.ReleaseRecords.Add(new ReleaseRecord
                    {
                        SeasonId = season.Id, FranchiseId = f.Id, PlayerId = p.Id, Reason = $"Not retained ({req.Source})", Status = ReleaseStatus.Released,
                        ReleasedAt = now, ReleasedBy = me.UserId,
                    });
                    released++;
                }

                // Confirmed retention list (replaces any earlier unsubmitted plans for the season).
                var oldPlans = await db.RetentionPlans.IgnoreQueryFilters().Include(x => x.Records).Where(x => x.FranchiseId == f.Id && x.SeasonId == season.Id).ToListAsync(ct);
                foreach (var old in oldPlans) { db.RetentionRecords.RemoveRange(old.Records); db.RetentionPlans.Remove(old); }
                var plan = new RetentionPlan { FranchiseId = f.Id, SeasonId = season.Id, Name = "Official retention list", IsSubmitted = true, SubmittedAt = now };
                db.RetentionPlans.Add(plan);
                var slot = 0;
                foreach (var pid in keep)
                {
                    var row = squads.First(s => s.PlayerId == pid);
                    db.RetentionRecords.Add(new RetentionRecord
                    {
                        RetentionPlanId = plan.Id, SeasonId = season.Id, FranchiseId = f.Id, PlayerId = pid,
                        Category = players[pid].IsCapped ? RetentionCategory.Capped : RetentionCategory.Uncapped, SlotNumber = ++slot,
                        Cost = row.Price, Status = RetentionStatus.Confirmed, CreatedAt = now,
                    });
                    retained++;
                }

                // Purse: set the official remaining amount through the ledger (difference as one entry).
                if (team.PurseRemaining is { } target)
                {
                    var fs = await db.FranchiseSeasons.FirstOrDefaultAsync(x => x.FranchiseId == f.Id && x.SeasonId == season.Id, ct);
                    if (fs is null) db.FranchiseSeasons.Add(fs = new FranchiseSeason { FranchiseId = f.Id, SeasonId = season.Id, PurseRemaining = 0 });
                    if (req.AuctionPurse is { } purseTotal) fs.PurseTotal = purseTotal;
                    var delta = target - fs.PurseRemaining;
                    if (delta != 0)
                        await Ledger.PostAsync(db, f.Id, season.Id, FinanceCategory.AuctionPurse, delta > 0 ? FinanceDirection.Credit : FinanceDirection.Debit, Math.Abs(delta),
                            $"Official purse after retentions ({req.Source})", "OfficialSquadUpdate", season.Id.ToString(), $"official-purse:{season.Id}:{f.Id}:{now:yyyyMMddHHmmss}",
                            me.UserId, now, ct, allowNegativePurse: true);
                    purses++;
                }
                audit.Record("squad.official_list", "Franchise", f.Id, new { retained = keep.Count, req.Source }, f.Id);
            }

            if (req.AuctionPurse is { } total)
            {
                var rule = await db.SeasonRules.FirstOrDefaultAsync(r => r.SeasonId == season.Id && r.Key == RuleKeys.AuctionPurse, ct);
                if (rule is null) db.SeasonRules.Add(rule = new SeasonRule { SeasonId = season.Id, Key = RuleKeys.AuctionPurse, ValueType = RuleValueType.Decimal, Category = "Auction" });
                rule.Value = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        await rules.InvalidateAsync(season.Id);
        Analytics.RatingService.Invalidate();
        return new OfficialSquadUpdateResult(transfers, released, retained, purses, notes);
    }
}
