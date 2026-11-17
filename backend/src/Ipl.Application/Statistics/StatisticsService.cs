using Ipl.Application.Players;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Statistics;

/// <summary>Batting/bowling/fielding totals with derived rates (null when not computable).</summary>
public record StatLineDto(
    int Matches, int Innings, int NotOuts, int Runs, int BallsFaced, int HighestScore, bool HighestNotOut, decimal? BattingAverage,
    decimal? StrikeRate, int Hundreds, int Fifties, int Fours, int Sixes, int BowlingInnings, int BallsBowled, int RunsConceded,
    int Wickets, decimal? BowlingAverage, decimal? Economy, decimal? BowlingStrikeRate, string? BestBowling, int FourWicketHauls,
    int FiveWicketHauls, int Catches, int Stumpings)
{
    public static StatLineDto From(StatisticTotals t) => new(
        t.Matches, t.Innings, t.NotOuts, t.Runs, t.BallsFaced, t.HighestScore, t.HighestNotOut,
        Rate(t.Runs, t.Innings - t.NotOuts), Rate(t.Runs * 100m, t.BallsFaced), t.Hundreds, t.Fifties, t.Fours, t.Sixes,
        t.BowlingInnings, t.BallsBowled, t.RunsConceded, t.Wickets, Rate(t.RunsConceded, t.Wickets), Rate(t.RunsConceded * 6m, t.BallsBowled),
        Rate(t.BallsBowled, t.Wickets), t.BestBowlingWickets > 0 || t.BowlingInnings > 0 ? $"{t.BestBowlingWickets}/{t.BestBowlingRuns}" : null,
        t.FourWicketHauls, t.FiveWicketHauls, t.Catches, t.Stumpings);

    public static decimal? Rate(decimal numerator, decimal denominator) => denominator <= 0 ? null : Math.Round(numerator / denominator, 2);
}

public record SeasonStatDto(int Year, string Competition, string CompetitionCode, string Format, string? Team, StatLineDto Line, string DataSource, DateTime LastUpdatedAt);

public record CareerStatDto(string Competition, string CompetitionCode, string Type, string Format, int FirstYear, int LastYear, StatLineDto Line, string DataSource, DateTime LastUpdatedAt);

public record PlayerStatisticsDto(
    Guid PlayerId, string PlayerName, bool IsDemo,
    IReadOnlyList<CareerStatDto> Careers, IReadOnlyList<SeasonStatDto> Seasons, SeasonStatDto? LastPrimaryT20Season);

public record FormMatchDto(DateOnly Date, string Competition, string Opponent, string Venue, bool IsHome, int Runs, int Balls, bool NotOut,
    bool Batted, int Wickets, int RunsConceded, int BallsBowled, bool Bowled);

public record SplitDto(string Label, int Matches, int Runs, int Balls, int Dismissals, int Wickets, int BallsBowled, int RunsConceded)
{
    public decimal? StrikeRate => StatLineDto.Rate(Runs * 100m, Balls);
    public decimal? Average => StatLineDto.Rate(Runs, Dismissals);
    public decimal? Economy => StatLineDto.Rate(RunsConceded * 6m, BallsBowled);
    public decimal? BowlingAverage => StatLineDto.Rate(RunsConceded, Wickets);
}

public record PhaseDto(string Phase, int Runs, int Balls, decimal? StrikeRate, int BallsBowled, int RunsConceded, int Wickets, decimal? Economy);

public record FormSummaryDto(int Matches, int Runs, decimal? BattingAverage, decimal? StrikeRate, int Wickets, decimal? Economy, decimal? BowlingAverage);

public record PerformanceAnalysisDto(
    Guid PlayerId, string Scope, IReadOnlyList<string> DataSources, DateTime? LastUpdatedAt, int MatchesAnalysed,
    FormSummaryDto Last5, FormSummaryDto Last10, FormSummaryDto Last20, string FormTrend,
    IReadOnlyList<FormMatchDto> RecentMatches,
    IReadOnlyList<SplitDto> ByYear, SplitDto Home, SplitDto Away,
    IReadOnlyList<SplitDto> ByOpponent, IReadOnlyList<SplitDto> ByVenue,
    IReadOnlyList<PhaseDto> Phases,
    decimal? BoundaryPercentage, int RunsVsPace, int BallsVsPace, decimal? StrikeRateVsPace, int RunsVsSpin, int BallsVsSpin, decimal? StrikeRateVsSpin,
    IReadOnlyList<SplitDto> Dismissals,
    decimal? DotBallPercentage, int WicketsVsRightHand, int WicketsVsLeftHand);

public record SeasonStatImportResult(int Imported, int Skipped, IReadOnlyList<string> Errors);

public class StatisticsService(IAppDbContext db, ICurrentUser me, IAppCache cache, IAuditService audit, IClock clock, PlayerService players)
{
    public async Task<PlayerStatisticsDto> GetAsync(Guid playerId, CancellationToken ct)
    {
        me.Require(Perm.StatsRead);
        return await cache.GetOrCreateAsync($"stats:summary:{playerId}", async () =>
        {
            var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId, ct) ?? throw new NotFoundException("Player");
            var careers = await db.PlayerCareerStatistics.AsNoTracking().Include(c => c.Competition).Where(c => c.PlayerId == playerId).ToListAsync(ct);
            var seasons = await db.PlayerSeasonStatistics.AsNoTracking().Include(c => c.Competition).Where(c => c.PlayerId == playerId)
                .OrderByDescending(s => s.Year).ThenBy(s => s.Competition!.Type).ToListAsync(ct);
            var primary = await players.PrimaryT20CompetitionIdAsync(ct);
            var seasonDtos = seasons.Select(s => new SeasonStatDto(s.Year, s.Competition!.Name, s.Competition.Code, s.Competition.Format.ToString(),
                s.TeamName, StatLineDto.From(s), s.DataSource, s.LastUpdatedAt)).ToList();
            return new PlayerStatisticsDto(playerId, player.FullName, player.IsDemo,
                careers.OrderBy(c => c.Competition!.Type).ThenByDescending(c => c.Competition!.Level)
                    .Select(c => new CareerStatDto(c.Competition!.Name, c.Competition.Code, c.Competition.Type.ToString(), c.Competition.Format.ToString(),
                        c.FirstYear, c.LastYear, StatLineDto.From(c), c.DataSource, c.LastUpdatedAt)).ToList(),
                seasonDtos,
                seasons.Where(s => s.CompetitionId == primary).OrderByDescending(s => s.Year)
                    .Select(s => seasonDtos[seasons.IndexOf(s)]).FirstOrDefault());
        }, TimeSpan.FromMinutes(15), ct);
    }

    /// <summary>Match-level analysis; competitionId null = all T20 matches (franchise + domestic + T20I).</summary>
    public async Task<PerformanceAnalysisDto> AnalyseAsync(Guid playerId, int? competitionId, CancellationToken ct)
    {
        me.Require(Perm.StatsRead);
        return await cache.GetOrCreateAsync($"stats:analysis:{playerId}:{competitionId}", async () =>
        {
            if (!await db.Players.AnyAsync(p => p.Id == playerId, ct)) throw new NotFoundException("Player");
            var q = db.PlayerMatchStatistics.AsNoTracking().Where(s => s.PlayerId == playerId);
            string scope;
            if (competitionId is { } cid)
            {
                q = q.Where(s => s.Match!.CompetitionId == cid);
                scope = await db.Competitions.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "Competition";
            }
            else
            {
                q = q.Where(s => s.Match!.Competition!.Format == MatchFormat.T20 || s.Match.Competition.Format == MatchFormat.T20I);
                scope = "All T20 cricket";
            }
            var rows = await q.Select(s => new
            {
                S = s, s.Match!.Date, s.Match.Year, Competition = s.Match.Competition!.Name, s.Match.DataSource,
            }).OrderByDescending(x => x.Date).ToListAsync(ct);
            var stats = rows.Select(r => r.S).ToList();
            var lastUpdated = await db.PlayerSeasonStatistics.Where(s => s.PlayerId == playerId).MaxAsync(s => (DateTime?)s.LastUpdatedAt, ct);

            FormSummaryDto Form(int n)
            {
                var take = stats.Take(n).ToList();
                var runs = take.Sum(s => s.Runs);
                var balls = take.Sum(s => s.BallsFaced);
                var outs = take.Count(s => s.Dismissed);
                var wk = take.Sum(s => s.Wickets);
                var bb = take.Sum(s => s.BallsBowled);
                var rc = take.Sum(s => s.RunsConceded);
                return new FormSummaryDto(take.Count, runs, StatLineDto.Rate(runs, outs), StatLineDto.Rate(runs * 100m, balls), wk,
                    StatLineDto.Rate(rc * 6m, bb), StatLineDto.Rate(rc, wk));
            }

            SplitDto Split(string label, IEnumerable<PlayerMatchStatistic> set)
            {
                var list = set.ToList();
                return new SplitDto(label, list.Count, list.Sum(s => s.Runs), list.Sum(s => s.BallsFaced), list.Count(s => s.Dismissed),
                    list.Sum(s => s.Wickets), list.Sum(s => s.BallsBowled), list.Sum(s => s.RunsConceded));
            }

            var last5 = Form(5);
            var last20 = Form(20);
            var trend = TrendLabel(last5, last20);
            var totalRuns = stats.Sum(s => s.Runs);
            var boundaryRuns = stats.Sum(s => s.Fours * 4 + s.Sixes * 6);
            var bowled = stats.Sum(s => s.BallsBowled);

            return new PerformanceAnalysisDto(
                playerId, scope, rows.Select(r => r.DataSource).Distinct().ToList(), lastUpdated, stats.Count,
                last5, Form(10), last20, trend,
                rows.Take(20).Select(r => new FormMatchDto(r.Date, r.Competition, r.S.Opponent, r.S.Venue, r.S.IsHome, r.S.Runs, r.S.BallsFaced,
                    r.S.Batted && !r.S.Dismissed, r.S.Batted, r.S.Wickets, r.S.RunsConceded, r.S.BallsBowled, r.S.Bowled)).ToList(),
                rows.GroupBy(r => r.Year).OrderBy(g => g.Key).Select(g => Split(g.Key.ToString(), g.Select(x => x.S))).ToList(),
                Split("Home", stats.Where(s => s.IsHome)), Split("Away", stats.Where(s => !s.IsHome)),
                stats.GroupBy(s => s.Opponent).Select(g => Split(g.Key, g)).OrderByDescending(s => s.Matches).Take(12).ToList(),
                stats.GroupBy(s => s.Venue).Select(g => Split(g.Key, g)).OrderByDescending(s => s.Matches).Take(12).ToList(),
                [
                    Phase("Powerplay", stats.Sum(s => s.PowerplayRuns), stats.Sum(s => s.PowerplayBalls), stats.Sum(s => s.PowerplayBallsBowled), stats.Sum(s => s.PowerplayRunsConceded), stats.Sum(s => s.PowerplayWickets)),
                    Phase("Middle overs", stats.Sum(s => s.MiddleRuns), stats.Sum(s => s.MiddleBalls), stats.Sum(s => s.MiddleBallsBowled), stats.Sum(s => s.MiddleRunsConceded), stats.Sum(s => s.MiddleWickets)),
                    Phase("Death overs", stats.Sum(s => s.DeathRuns), stats.Sum(s => s.DeathBalls), stats.Sum(s => s.DeathBallsBowled), stats.Sum(s => s.DeathRunsConceded), stats.Sum(s => s.DeathWickets)),
                ],
                StatLineDto.Rate(boundaryRuns * 100m, totalRuns),
                stats.Sum(s => s.RunsVsPace), stats.Sum(s => s.BallsVsPace), StatLineDto.Rate(stats.Sum(s => s.RunsVsPace) * 100m, stats.Sum(s => s.BallsVsPace)),
                stats.Sum(s => s.RunsVsSpin), stats.Sum(s => s.BallsVsSpin), StatLineDto.Rate(stats.Sum(s => s.RunsVsSpin) * 100m, stats.Sum(s => s.BallsVsSpin)),
                stats.Where(s => s.Dismissed && s.DismissalType != null).GroupBy(s => s.DismissalType!)
                    .Select(g => new SplitDto(g.Key, g.Count(), 0, 0, g.Count(), 0, 0, 0)).OrderByDescending(s => s.Matches).ToList(),
                StatLineDto.Rate(stats.Sum(s => s.DotBalls) * 100m, bowled),
                stats.Sum(s => s.WicketsVsRightHand), stats.Sum(s => s.WicketsVsLeftHand));
        }, TimeSpan.FromMinutes(15), ct);
    }

    private static PhaseDto Phase(string name, int runs, int balls, int ballsBowled, int conceded, int wickets) =>
        new(name, runs, balls, StatLineDto.Rate(runs * 100m, balls), ballsBowled, conceded, wickets, StatLineDto.Rate(conceded * 6m, ballsBowled));

    /// <summary>Compares the last 5 matches with the last 20 (runs per match and wickets per match).</summary>
    public static string TrendLabel(FormSummaryDto last5, FormSummaryDto last20)
    {
        if (last5.Matches < 3 || last20.Matches < 8) return "Not enough recent matches";
        var r5 = (decimal)last5.Runs / last5.Matches + 15m * last5.Wickets / last5.Matches;
        var r20 = (decimal)last20.Runs / last20.Matches + 15m * last20.Wickets / last20.Matches;
        if (r20 == 0) return r5 > 0 ? "Improving" : "Stable";
        var change = (r5 - r20) / r20;
        return change switch { > 0.2m => "Improving", < -0.2m => "Declining", _ => "Stable" };
    }

    /// <summary>
    /// Rebuilds season rows from match rows (for player/competition/years that have match data) and career
    /// rows from season rows. Imported season rows without match data are kept as they are.
    /// </summary>
    public async Task RecalculateAsync(IReadOnlyCollection<Guid>? playerIds, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var matchQuery = db.PlayerMatchStatistics.AsNoTracking().AsQueryable();
        if (playerIds is not null) matchQuery = matchQuery.Where(s => playerIds.Contains(s.PlayerId));
        var grouped = await matchQuery
            .Select(s => new { S = s, s.Match!.CompetitionId, s.Match.Year, s.Match.DataSource })
            .ToListAsync(ct);

        var seasonQuery = db.PlayerSeasonStatistics.AsQueryable();
        if (playerIds is not null) seasonQuery = seasonQuery.Where(s => playerIds.Contains(s.PlayerId));
        var existingSeasons = await seasonQuery.ToListAsync(ct);
        var seasonIndex = existingSeasons.ToDictionary(s => (s.PlayerId, s.CompetitionId, s.Year));

        foreach (var g in grouped.GroupBy(x => (x.S.PlayerId, x.CompetitionId, x.Year)))
        {
            if (!seasonIndex.TryGetValue(g.Key, out var row))
            {
                row = new PlayerSeasonStatistic { PlayerId = g.Key.PlayerId, CompetitionId = g.Key.CompetitionId, Year = g.Key.Year };
                db.PlayerSeasonStatistics.Add(row);
                seasonIndex[g.Key] = row;
            }
            var list = g.Select(x => x.S).ToList();
            Aggregate(row, list);
            row.TeamName = list.GroupBy(s => s.Team).OrderByDescending(t => t.Count()).First().Key;
            row.DataSource = string.Join("; ", g.Select(x => x.DataSource).Distinct());
            row.LastUpdatedAt = now;
        }

        var careerQuery = db.PlayerCareerStatistics.AsQueryable();
        if (playerIds is not null) careerQuery = careerQuery.Where(s => playerIds.Contains(s.PlayerId));
        var careers = (await careerQuery.ToListAsync(ct)).ToDictionary(c => (c.PlayerId, c.CompetitionId));
        foreach (var g in seasonIndex.Values.GroupBy(s => (s.PlayerId, s.CompetitionId)))
        {
            if (!careers.TryGetValue(g.Key, out var career))
            {
                career = new PlayerCareerStatistic { PlayerId = g.Key.PlayerId, CompetitionId = g.Key.CompetitionId };
                db.PlayerCareerStatistics.Add(career);
            }
            Sum(career, g.ToList());
            career.FirstYear = g.Min(s => s.Year);
            career.LastYear = g.Max(s => s.Year);
            career.DataSource = string.Join("; ", g.Select(s => s.DataSource).Distinct());
            career.LastUpdatedAt = g.Max(s => s.LastUpdatedAt);
        }
        await db.SaveChangesAsync(ct);
        await cache.BumpAsync("stats", ct);
        Analytics.RatingService.Invalidate();
    }

    /// <summary>
    /// Imports season totals from an authorised provider (CSV). Columns: player_code, competition_code, year, team, matches,
    /// innings, not_outs, runs, balls_faced, highest_score, highest_not_out, hundreds, fifties, fours, sixes, bowling_innings,
    /// balls_bowled, runs_conceded, wickets, best_wickets, best_runs, four_wicket_hauls, five_wicket_hauls, catches, stumpings, source.
    /// </summary>
    public async Task<SeasonStatImportResult> ImportSeasonCsvAsync(Stream csv, CancellationToken ct)
    {
        me.Require(Perm.StatsImport);
        var rows = Csv.Read(csv);
        var players = await db.Players.Select(p => new { p.Code, p.Id }).ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        var comps = await db.Competitions.ToDictionaryAsync(c => c.Code.ToUpperInvariant(), c => c.Id, ct);
        int imported = 0, skipped = 0;
        List<string> errors = [];
        HashSet<Guid> touched = [];
        var now = clock.UtcNow;
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            try
            {
                if (!players.TryGetValue(r.Get("player_code"), out var pid)) throw new FormatException($"unknown player '{r.Get("player_code")}'.");
                if (!comps.TryGetValue(r.Get("competition_code").ToUpperInvariant(), out var cid)) throw new FormatException($"unknown competition '{r.Get("competition_code")}'.");
                var source = r.Get("source");
                if (source.Length == 0) throw new FormatException("source is required.");
                var year = r.Int("year");
                if (year < 1990 || year > clock.Today.Year) throw new FormatException("year is out of range.");
                var row = await db.PlayerSeasonStatistics.FirstOrDefaultAsync(s => s.PlayerId == pid && s.CompetitionId == cid && s.Year == year, ct);
                if (row is null) db.PlayerSeasonStatistics.Add(row = new PlayerSeasonStatistic { PlayerId = pid, CompetitionId = cid, Year = year });
                row.TeamName = r.Get("team");
                row.Matches = r.Int("matches"); row.Innings = r.Int("innings"); row.NotOuts = r.Int("not_outs"); row.Runs = r.Int("runs");
                row.BallsFaced = r.Int("balls_faced"); row.HighestScore = r.Int("highest_score"); row.HighestNotOut = Csv.Bool(r.Get("highest_not_out"));
                row.Hundreds = r.Int("hundreds"); row.Fifties = r.Int("fifties"); row.Fours = r.Int("fours"); row.Sixes = r.Int("sixes");
                row.BowlingInnings = r.Int("bowling_innings"); row.BallsBowled = r.Int("balls_bowled"); row.RunsConceded = r.Int("runs_conceded");
                row.Wickets = r.Int("wickets"); row.BestBowlingWickets = r.Int("best_wickets"); row.BestBowlingRuns = r.Int("best_runs");
                row.FourWicketHauls = r.Int("four_wicket_hauls"); row.FiveWicketHauls = r.Int("five_wicket_hauls");
                row.Catches = r.Int("catches"); row.Stumpings = r.Int("stumpings");
                if (row.NotOuts > row.Innings || row.Innings > row.Matches) throw new FormatException("innings/not-outs are inconsistent with matches.");
                row.DataSource = source;
                row.LastUpdatedAt = now;
                touched.Add(pid);
                imported++;
            }
            catch (FormatException ex)
            {
                skipped++;
                errors.Add($"Line {i + 2}: {ex.Message}");
            }
        }
        audit.Record("stats.imported", "PlayerSeasonStatistic", null, new { imported, skipped });
        await db.SaveChangesAsync(ct);
        if (touched.Count > 0) await RecalculateAsync(touched, ct);
        return new SeasonStatImportResult(imported, skipped, errors.Take(50).ToList());
    }

    public static void Aggregate(StatisticTotals row, IReadOnlyList<PlayerMatchStatistic> list)
    {
        row.Matches = list.Count;
        row.Innings = list.Count(s => s.Batted);
        row.NotOuts = list.Count(s => s.Batted && !s.Dismissed);
        row.Runs = list.Sum(s => s.Runs);
        row.BallsFaced = list.Sum(s => s.BallsFaced);
        var top = list.Where(s => s.Batted).OrderByDescending(s => s.Runs).ThenBy(s => s.Dismissed).FirstOrDefault();
        row.HighestScore = top?.Runs ?? 0;
        row.HighestNotOut = top is { Dismissed: false };
        row.Hundreds = list.Count(s => s.Runs >= 100);
        row.Fifties = list.Count(s => s.Runs is >= 50 and < 100);
        row.Fours = list.Sum(s => s.Fours);
        row.Sixes = list.Sum(s => s.Sixes);
        row.BowlingInnings = list.Count(s => s.Bowled);
        row.BallsBowled = list.Sum(s => s.BallsBowled);
        row.RunsConceded = list.Sum(s => s.RunsConceded);
        row.Wickets = list.Sum(s => s.Wickets);
        var best = list.Where(s => s.Bowled).OrderByDescending(s => s.Wickets).ThenBy(s => s.RunsConceded).FirstOrDefault();
        row.BestBowlingWickets = best?.Wickets ?? 0;
        row.BestBowlingRuns = best?.RunsConceded ?? 0;
        row.FourWicketHauls = list.Count(s => s.Wickets == 4);
        row.FiveWicketHauls = list.Count(s => s.Wickets >= 5);
        row.Catches = list.Sum(s => s.Catches);
        row.Stumpings = list.Sum(s => s.Stumpings);
    }

    public static void Sum(StatisticTotals target, IReadOnlyList<StatisticTotals> rows)
    {
        target.Matches = rows.Sum(r => r.Matches);
        target.Innings = rows.Sum(r => r.Innings);
        target.NotOuts = rows.Sum(r => r.NotOuts);
        target.Runs = rows.Sum(r => r.Runs);
        target.BallsFaced = rows.Sum(r => r.BallsFaced);
        var top = rows.OrderByDescending(r => r.HighestScore).ThenBy(r => r.HighestNotOut ? 0 : 1).FirstOrDefault();
        target.HighestScore = top?.HighestScore ?? 0;
        target.HighestNotOut = top?.HighestNotOut ?? false;
        target.Hundreds = rows.Sum(r => r.Hundreds);
        target.Fifties = rows.Sum(r => r.Fifties);
        target.Fours = rows.Sum(r => r.Fours);
        target.Sixes = rows.Sum(r => r.Sixes);
        target.BowlingInnings = rows.Sum(r => r.BowlingInnings);
        target.BallsBowled = rows.Sum(r => r.BallsBowled);
        target.RunsConceded = rows.Sum(r => r.RunsConceded);
        target.Wickets = rows.Sum(r => r.Wickets);
        var best = rows.OrderByDescending(r => r.BestBowlingWickets).ThenBy(r => r.BestBowlingRuns).FirstOrDefault();
        target.BestBowlingWickets = best?.BestBowlingWickets ?? 0;
        target.BestBowlingRuns = best?.BestBowlingRuns ?? 0;
        target.FourWicketHauls = rows.Sum(r => r.FourWicketHauls);
        target.FiveWicketHauls = rows.Sum(r => r.FiveWicketHauls);
        target.Catches = rows.Sum(r => r.Catches);
        target.Stumpings = rows.Sum(r => r.Stumpings);
    }
}
