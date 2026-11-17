using System.Text.Json;
using Ipl.Application.Players;
using Ipl.Application.Rules;
using Ipl.Application.Squads;
using Ipl.Application.Statistics;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Analytics;

public record ComparedPlayerDto(
    Guid PlayerId, string Name, int? Age, string Country, string Role, string? Franchise, string Availability, bool IsOverseas,
    StatLineDto? T20Franchise, StatLineDto? Domestic, StatLineDto? International, FormSummaryDto RecentForm, int FranchiseT20Matches,
    decimal Rating, decimal? EstimatedValue, string? ValueRange, IReadOnlyDictionary<string, decimal> Radar, IReadOnlyList<string> DataSources);

public record ComparisonResultDto(IReadOnlyList<ComparedPlayerDto> Players, IReadOnlyList<string> RadarAxes, string Note);

public record SavedComparisonDto(Guid Id, string Name, IReadOnlyList<Guid> PlayerIds, IReadOnlyList<string> PlayerNames, string? Notes, bool IsShared, string CreatedBy, DateTime CreatedAt, bool IsMine);

public record SaveComparisonRequest(string Name, IReadOnlyList<Guid> PlayerIds, string? Notes, bool IsShared);

public class ComparisonService(IAppDbContext db, ICurrentUser me, IClock clock, RatingService ratings, ValuationService valuations, StatisticsService stats, IAuditService audit)
{
    public static readonly string[] Axes = ["Batting average", "Strike rate", "Wickets per match", "Economy", "Recent form", "Experience"];

    public async Task<ComparisonResultDto> CompareAsync(IReadOnlyList<Guid> playerIds, CancellationToken ct)
    {
        me.Require(Perm.ComparisonsRead);
        var ids = playerIds.Distinct().ToList();
        if (ids.Count is < 2 or > 5) throw new AppValidationException("playerIds", "Compare between 2 and 5 players.");
        var players = await db.Players.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.DateOfBirth, Country = p.Country!.Name, Role = p.PrimaryRole!.Name, Franchise = p.CurrentFranchise != null ? p.CurrentFranchise.Name : null, p.IsOverseas })
            .ToListAsync(ct);
        if (players.Count != ids.Count) throw new NotFoundException("Player");
        var careers = await db.PlayerCareerStatistics.AsNoTracking().Include(c => c.Competition).Where(c => ids.Contains(c.PlayerId)).ToListAsync(ct);
        var avail = await db.PlayerAvailability.AsNoTracking().Where(a => ids.Contains(a.PlayerId)).ToDictionaryAsync(a => a.PlayerId, a => a.Status.ToString(), ct);
        var rated = await ratings.RateManyAsync(ids, ct);
        Dictionary<Guid, ValuationDto> values = [];
        if (me.Has(Perm.ValuationRead))
            foreach (var v in await valuations.RunManyAsync(ids, ct)) values[v.PlayerId] = v;

        StatLineDto? Line(Guid id, CompetitionType type, params MatchFormat[] formats)
        {
            var rows = careers.Where(c => c.PlayerId == id && c.Competition!.Type == type && formats.Contains(c.Competition.Format)).Cast<StatisticTotals>().ToList();
            if (rows.Count == 0) return null;
            var sum = new PlayerCareerStatistic();
            StatisticsService.Sum(sum, rows);
            return StatLineDto.From(sum);
        }

        List<ComparedPlayerDto> result = [];
        foreach (var id in ids)
        {
            var p = players.First(x => x.Id == id);
            var analysis = await stats.AnalyseAsync(id, null, ct);
            var t20 = Line(id, CompetitionType.Franchise, MatchFormat.T20);
            var all = careers.Where(c => c.PlayerId == id && (c.Competition!.Format == MatchFormat.T20 || c.Competition.Format == MatchFormat.T20I)).Cast<StatisticTotals>().ToList();
            var allSum = new PlayerCareerStatistic();
            StatisticsService.Sum(allSum, all);
            var line = StatLineDto.From(allSum);
            var radar = new Dictionary<string, decimal>
            {
                ["Batting average"] = Scale(line.BattingAverage, 0, 45),
                ["Strike rate"] = Scale(line.StrikeRate, 90, 170),
                ["Wickets per match"] = Scale(allSum.Matches > 0 ? allSum.Wickets / (decimal)allSum.Matches : null, 0, 1.8m),
                ["Economy"] = line.Economy is null ? 0 : 100 - Scale(line.Economy, 6, 11),
                ["Recent form"] = rated.TryGetValue(id, out var r) ? r.Factors.First(f => f.Factor == "Recent form").Score : 0,
                ["Experience"] = Scale(allSum.Matches, 0, 200),
            };
            values.TryGetValue(id, out var val);
            result.Add(new ComparedPlayerDto(id, p.FullName, PlayerService.AgeOn(p.DateOfBirth, clock.Today), p.Country, p.Role, p.Franchise,
                avail.GetValueOrDefault(id, "Available"), p.IsOverseas, t20, Line(id, CompetitionType.Domestic, MatchFormat.T20, MatchFormat.ListA, MatchFormat.FirstClass),
                Line(id, CompetitionType.International, MatchFormat.T20I, MatchFormat.ODI, MatchFormat.Test), analysis.Last10, t20?.Matches ?? 0,
                r?.Rating ?? 0, val?.Mid, val is null ? null : $"{Money.Format(val.Low)} – {Money.Format(val.High)}", radar,
                careers.Where(c => c.PlayerId == id).Select(c => c.DataSource).Distinct().ToList()));
        }
        return new ComparisonResultDto(result, Axes,
            "Radar values are scaled 0–100 from recorded T20 statistics (all T20 competitions). Valuations are model estimates, not official prices.");
    }

    public async Task<IReadOnlyList<SavedComparisonDto>> SavedAsync(CancellationToken ct)
    {
        me.Require(Perm.ComparisonsRead);
        var fid = me.RequireFranchise();
        var rows = await db.PlayerComparisons.AsNoTracking().Where(c => c.FranchiseId == fid && (c.IsShared || c.CreatedBy == me.UserId))
            .OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        var allIds = rows.SelectMany(r => JsonSerializer.Deserialize<List<Guid>>(r.PlayerIds) ?? []).Distinct().ToList();
        var names = await db.Players.AsNoTracking().Where(p => allIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FullName, ct);
        var authors = rows.Select(r => r.CreatedBy).OfType<Guid>().Distinct().ToList();
        var authorNames = await db.Users.AsNoTracking().Where(u => authors.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows.Select(r =>
        {
            var ids = JsonSerializer.Deserialize<List<Guid>>(r.PlayerIds) ?? [];
            return new SavedComparisonDto(r.Id, r.Name, ids, ids.Select(i => names.GetValueOrDefault(i, "Removed player")).ToList(), r.Notes, r.IsShared,
                r.CreatedBy is { } cb ? authorNames.GetValueOrDefault(cb, "") : "", r.CreatedAt, r.CreatedBy == me.UserId);
        }).ToList();
    }

    public async Task<Guid> SaveAsync(SaveComparisonRequest req, CancellationToken ct)
    {
        me.Require(Perm.ComparisonsManage);
        var fid = me.RequireFranchise();
        if (string.IsNullOrWhiteSpace(req.Name)) throw new AppValidationException("name", "Name the comparison.");
        if (req.PlayerIds.Distinct().Count() is < 2 or > 5) throw new AppValidationException("playerIds", "Save between 2 and 5 players.");
        var c = new PlayerComparison { FranchiseId = fid, Name = req.Name.Trim(), PlayerIds = JsonSerializer.Serialize(req.PlayerIds.Distinct()), Notes = req.Notes?.Trim(), IsShared = req.IsShared };
        db.PlayerComparisons.Add(c);
        audit.Record("comparisons.saved", "PlayerComparison", c.Id, new { c.Name, req.IsShared }, fid);
        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        me.Require(Perm.ComparisonsManage);
        var c = await db.PlayerComparisons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Comparison");
        if (c.CreatedBy != me.UserId) throw new ForbiddenException("Only the author can delete a saved comparison.");
        db.PlayerComparisons.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    private static decimal Scale(decimal? value, decimal min, decimal max) =>
        value is null ? 0 : Math.Round(Math.Clamp((value.Value - min) / (max - min) * 100, 0, 100), 1);
}

public record ValueGapDto(Guid PlayerId, string Name, string Role, string? Franchise, decimal Rating, decimal ModelMid, decimal ReferencePrice, decimal Gap, string Reference);

public record AuctionTargetDto(Guid PlayerId, string Name, string Role, bool IsOverseas, int? Age, decimal Rating, decimal Low, decimal Mid, decimal High,
    decimal? BasePrice, string FitReason, int Priority);

public record SquadNeedDto(string Need, string Detail, int Severity);

public record StrategyDto(decimal PurseRemaining, int SlotsRemaining, int OverseasSlotsRemaining, IReadOnlyList<SquadNeedDto> Needs,
    IReadOnlyList<AuctionTargetDto> Targets, IReadOnlyList<string> Suggestions, string Disclaimer);

public record TradeValueDto(decimal InitiatorGives, decimal InitiatorReceives, decimal Balance, string Verdict, IReadOnlyList<ValuationDto> Valuations, string Disclaimer);

public class RecommendationService(IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, RatingService ratings, ValuationService valuations)
{
    /// <summary>Players whose model value is far above (undervalued) or below (overvalued) their current price.</summary>
    public async Task<IReadOnlyList<ValueGapDto>> ValueGapsAsync(bool undervalued, CancellationToken ct)
    {
        me.Require(Perm.ValuationRead);
        var season = await rules.CurrentSeasonAsync(ct);
        var priced = await db.FranchiseSquads.AsNoTracking().Where(s => s.SeasonId == season.Id && s.Status == SquadMemberStatus.Active && s.Price > 0)
            .Select(s => new { s.PlayerId, s.Price, Franchise = s.Franchise!.ShortName }).ToListAsync(ct);
        var vals = await valuations.RunManyAsync(priced.Select(p => p.PlayerId).ToList(), ct);
        var pricedIds = priced.Select(x => x.PlayerId).ToList();
        var roles = await db.Players.AsNoTracking().Where(p => pricedIds.Contains(p.Id))
            .Select(p => new { p.Id, Role = p.PrimaryRole!.Name }).ToDictionaryAsync(p => p.Id, p => p.Role, ct);
        var rows = vals.Select(v =>
        {
            var p = priced.First(x => x.PlayerId == v.PlayerId);
            return new ValueGapDto(v.PlayerId, v.PlayerName, roles.GetValueOrDefault(v.PlayerId, ""), p.Franchise, v.Rating, v.Mid, p.Price, v.Mid - p.Price, "Current squad price");
        });
        return (undervalued ? rows.Where(r => r.Gap > 0).OrderByDescending(r => r.Gap / r.ReferencePrice) : rows.Where(r => r.Gap < 0).OrderBy(r => r.Gap / r.ReferencePrice))
            .Take(25).ToList();
    }

    /// <summary>Squad gaps plus the best-fitting players in upcoming auction pools, with plain-language reasons.</summary>
    public async Task<StrategyDto> AuctionStrategyAsync(Guid? franchiseId, CancellationToken ct)
    {
        me.Require(Perm.ValuationRead);
        var fid = me.RequireFranchise(franchiseId);
        var season = await rules.CurrentSeasonAsync(ct);
        var r = await rules.ForSeasonAsync(season.Id, ct);
        var squad = await SquadMath.LoadAsync(db, fid, season.Id, clock.Today, ct);
        var comp = SquadMath.Compose(squad, r);
        var purse = await db.FranchiseSeasons.AsNoTracking().Where(s => s.FranchiseId == fid && s.SeasonId == season.Id).Select(s => s.PurseRemaining).FirstOrDefaultAsync(ct);
        var needs = Needs(comp, squad, r);

        var pool = await db.AuctionPlayers.AsNoTracking()
            .Where(l => l.Status == AuctionLotStatus.Pending && l.AuctionEvent!.Status != AuctionStatus.Completed && l.AuctionEvent.SeasonId == season.Id)
            .Select(l => new { l.PlayerId, l.BasePrice }).ToListAsync(ct);
        var poolIds = pool.Select(p => p.PlayerId).Distinct().ToList();
        if (poolIds.Count == 0)
            poolIds = await db.Players.AsNoTracking().Where(p => p.Status == PlayerStatus.AvailableForAuction && p.CurrentFranchiseId == null).Select(p => p.Id).Take(150).ToListAsync(ct);
        var vals = (await valuations.RunManyAsync(poolIds, ct)).ToDictionary(v => v.PlayerId);
        var info = await db.Players.AsNoTracking().Where(p => poolIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, Role = p.PrimaryRole!.Code, RoleName = p.PrimaryRole.Name, p.IsOverseas, p.IsWicketKeeper, p.BowlingType, p.DateOfBirth })
            .ToListAsync(ct);
        var needCodes = needs.Select(n => n.Need).ToHashSet();
        var targets = info.Where(p => vals.ContainsKey(p.Id) && (!p.IsOverseas || comp.OverseasSlotsRemaining > 0)).Select(p =>
        {
            var v = vals[p.Id];
            List<string> reasons = [];
            var priority = 0;
            if (needCodes.Contains("Wicketkeeper") && p.IsWicketKeeper) { reasons.Add("fills the wicketkeeper gap"); priority += 3; }
            if (needCodes.Contains("Bowling depth") && SquadMath.IsBowlingOption(p.Role, p.BowlingType)) { reasons.Add("adds bowling depth"); priority += 2; }
            if (needCodes.Contains("Spin") && p.BowlingType is BowlingType.OffSpin or BowlingType.LegSpin or BowlingType.LeftArmOrthodox or BowlingType.LeftArmWristSpin) { reasons.Add("adds spin"); priority += 2; }
            if (needCodes.Contains("Pace") && p.BowlingType is BowlingType.Fast or BowlingType.MediumFast) { reasons.Add("adds pace"); priority += 2; }
            if (needCodes.Contains("Batting depth") && p.Role is "batter" or "wk_batter" or "allrounder") { reasons.Add("adds batting depth"); priority += 1; }
            if (v.Rating >= 65) { reasons.Add($"strong rating ({v.Rating:0})"); priority += 1; }
            if (v.Mid > purse * 0.4m) { reasons.Add("expensive relative to purse"); priority -= 2; }
            return new AuctionTargetDto(p.Id, p.FullName, p.RoleName, p.IsOverseas, PlayerService.AgeOn(p.DateOfBirth, clock.Today), v.Rating, v.Low, v.Mid, v.High,
                pool.FirstOrDefault(x => x.PlayerId == p.Id)?.BasePrice, reasons.Count == 0 ? "General squad depth" : string.Join(", ", reasons), priority);
        }).OrderByDescending(t => t.Priority).ThenByDescending(t => t.Rating).Take(20).ToList();

        List<string> suggestions = [];
        if (comp.SlotsRemaining > 0)
        {
            var perSlot = purse / comp.SlotsRemaining;
            suggestions.Add($"Average budget per open slot: {Money.Format(perSlot)} ({comp.SlotsRemaining} slots, {Money.Format(purse)} purse).");
        }
        if (comp.Total < comp.MinSquad) suggestions.Add($"Buy at least {comp.MinSquad - comp.Total} more players to reach the minimum squad.");
        if (targets.Count > 0) suggestions.Add($"Top priority target: {targets[0].Name} ({targets[0].FitReason}), model range {Money.Format(targets[0].Low)}–{Money.Format(targets[0].High)}.");
        return new StrategyDto(purse, comp.SlotsRemaining, comp.OverseasSlotsRemaining, needs, targets, suggestions, ValuationService.Disclaimer);
    }

    public async Task<TradeValueDto> TradeValueAsync(IReadOnlyList<Guid> gives, IReadOnlyList<Guid> receives, decimal cashPaid, decimal cashReceived, CancellationToken ct)
    {
        me.Require(Perm.ValuationRead);
        var vals = await valuations.RunManyAsync(gives.Concat(receives).Distinct().ToList(), ct);
        var giveValue = vals.Where(v => gives.Contains(v.PlayerId)).Sum(v => v.Mid) + cashPaid;
        var getValue = vals.Where(v => receives.Contains(v.PlayerId)).Sum(v => v.Mid) + cashReceived;
        var balance = getValue - giveValue;
        var verdict = Math.Abs(balance) <= Math.Max(giveValue, getValue) * 0.1m ? "Roughly balanced"
            : balance > 0 ? "Favours you on model value" : "Favours the other franchise on model value";
        return new TradeValueDto(giveValue, getValue, balance, verdict, vals, ValuationService.Disclaimer);
    }

    public async Task<IReadOnlyList<SquadNeedDto>> SquadNeedsAsync(Guid? franchiseId, CancellationToken ct)
    {
        me.Require(Perm.SquadRead);
        var fid = me.RequireFranchise(franchiseId);
        var season = await rules.CurrentSeasonAsync(ct);
        var r = await rules.ForSeasonAsync(season.Id, ct);
        var squad = await SquadMath.LoadAsync(db, fid, season.Id, clock.Today, ct);
        return Needs(SquadMath.Compose(squad, r), squad, r);
    }

    /// <summary>Gaps compared with a balanced T20 squad. The minimums come from season rules where they exist.</summary>
    public static List<SquadNeedDto> Needs(CompositionDto comp, IReadOnlyList<SquadMember> squad, SeasonRules r)
    {
        List<SquadNeedDto> needs = [];
        if (comp.WicketKeepers < 2) needs.Add(new("Wicketkeeper", $"{comp.WicketKeepers} keeper(s); a back-up keeper is recommended.", comp.WicketKeepers == 0 ? 3 : 2));
        var bowlers = squad.Count(m => m.CanBowl);
        var minBowl = r.IntOr(RuleKeys.MinBowlingOptions, 5);
        if (bowlers < minBowl + 3) needs.Add(new("Bowling depth", $"{bowlers} bowling options for a rule of {minBowl} per XI plus rotation.", bowlers < minBowl ? 3 : 2));
        var batters = squad.Count(m => m.RoleCode is "batter" or "wk_batter");
        if (batters < 6) needs.Add(new("Batting depth", $"{batters} specialist batters.", batters < 4 ? 3 : 1));
        if (comp.AllRounders < 3) needs.Add(new("All-rounders", $"{comp.AllRounders} all-rounders.", 1));
        if (comp.Total < comp.MinSquad) needs.Add(new("Squad size", $"{comp.Total} of the {comp.MinSquad} minimum.", 3));
        return needs.OrderByDescending(n => n.Severity).ToList();
    }
}
