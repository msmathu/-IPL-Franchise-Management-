using System.Text.Json;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Statistics;

public record ScorecardDto(FixtureDto Match, JsonElement Card, IReadOnlyDictionary<string, Guid> LinkedPlayers);

public record LeaderRowDto(int Rank, string Name, Guid? PlayerId, string Team, string TeamCode, string Value, string Detail);

public record LeaderBoardDto(string Key, string Title, string Icon, IReadOnlyList<LeaderRowDto> Rows);

public record TeamRecordDto(string Team, string TeamCode, string Opponent, int Runs, int Wickets, string Overs, int Year, DateOnly Date, Guid MatchId);

public record RecordsDto(IReadOnlyList<LeaderBoardDto> Players, IReadOnlyList<TeamRecordDto> HighestTotals, IReadOnlyList<TeamRecordDto> LowestTotals, string Source);

public record HeadToHeadDto(
    TeamBadgeDto Team1, TeamBadgeDto Team2, int Played, int Team1Wins, int Team2Wins, int NoResult,
    TeamRecordDto? Team1Highest, TeamRecordDto? Team2Highest, IReadOnlyList<FixtureDto> Recent,
    IReadOnlyList<LeaderRowDto> TopRunScorers, IReadOnlyList<LeaderRowDto> TopWicketTakers, string Source);

public record MatchupDto(string BatterName, Guid? BatterId, string BowlerName, Guid? BowlerId, int Balls, int Runs, int Dismissals, int Fours, int Sixes,
    int Dots, decimal? StrikeRate, decimal? Average, decimal? DotPercentage);

public record PlayerMatchupsDto(Guid PlayerId, string PlayerName, IReadOnlyList<MatchupDto> AsBatterToughest, IReadOnlyList<MatchupDto> AsBatterFavourite,
    IReadOnlyList<MatchupDto> AsBowlerBunnies, IReadOnlyList<MatchupDto> AsBowlerNemesis, string Source);

/// <summary>Scorecards, leaderboards, all-time records, head-to-head and batter-v-bowler records (IPL), from imported ball-by-ball data.</summary>
public class RecordsService(IAppDbContext db, ICurrentUser me, IAppCache cache, SeasonCentreService season)
{
    private const string Competition = "IPL";
    private const int MinBallsForRate = 100;
    private const int MinBallsForEconomy = 120;
    private const int MinMatchupBalls = 12;

    public async Task<ScorecardDto> ScorecardAsync(Guid matchId, CancellationToken ct)
    {
        me.Require(Perm.StatsRead);
        var m = await db.Matches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == matchId, ct) ?? throw new NotFoundException("Match");
        if (m.ScorecardJson is null) throw new NotFoundException("Scorecard");
        var fixture = (await season.FixturesAsync(m.Year, null, ct)).FirstOrDefault(f => f.Id == matchId)
            ?? throw new NotFoundException("Match");
        using var doc = JsonDocument.Parse(m.ScorecardJson);
        var ids = new HashSet<string>();
        foreach (var inn in doc.RootElement.GetProperty("innings").EnumerateArray())
        {
            foreach (var b in inn.GetProperty("batting").EnumerateArray()) ids.Add(b.GetProperty("id").GetString()!);
            foreach (var b in inn.GetProperty("bowling").EnumerateArray()) ids.Add(b.GetProperty("id").GetString()!);
        }
        var linked = await db.Players.AsNoTracking().Where(p => p.CricsheetId != null && ids.Contains(p.CricsheetId))
            .ToDictionaryAsync(p => p.CricsheetId!, p => p.Id, ct);
        return new ScorecardDto(fixture, doc.RootElement.Clone(), linked);
    }

    public async Task<IReadOnlyList<LeaderBoardDto>> LeadersAsync(int year, CancellationToken ct)
    {
        me.Require(Perm.StatsRead);
        return await cache.GetOrCreateAsync($"stats:leaders:{year}", async () =>
        {
            var rows = await db.PersonSeasonStats.AsNoTracking().Where(s => s.Competition!.Code == Competition && s.Year == year).ToListAsync(ct);
            return (IReadOnlyList<LeaderBoardDto>)await BoardsAsync(rows.Select(Agg.From).ToList(), ct);
        }, TimeSpan.FromMinutes(30), ct);
    }

    public async Task<RecordsDto> RecordsAsync(CancellationToken ct)
    {
        me.Require(Perm.StatsRead);
        return await cache.GetOrCreateAsync("stats:records", async () =>
        {
            var rows = await db.PersonSeasonStats.AsNoTracking().Where(s => s.Competition!.Code == Competition).ToListAsync(ct);
            var careers = rows.GroupBy(r => r.PersonId).Select(g => Agg.Sum(g.OrderBy(r => r.Year).ToList())).ToList();
            var boards = await BoardsAsync(careers, ct, includeHundreds: true);
            var matches = await db.Matches.AsNoTracking().Where(m => m.Competition!.Code == Competition && m.ExternalRef != null && m.Team1Runs != null).ToListAsync(ct);
            var innings = matches.SelectMany(m => new[]
            {
                (m, Team: m.Team1, Opp: m.Team2, Runs: m.Team1Runs, Wkts: m.Team1Wickets, Balls: m.Team1Balls),
                (m, Team: m.Team2, Opp: m.Team1, Runs: m.Team2Runs, Wkts: m.Team2Wickets, Balls: m.Team2Balls),
            }).Where(x => x.Runs is not null).ToList();
            TeamRecordDto T((Match m, string Team, string Opp, int? Runs, int? Wkts, int? Balls) x) =>
                new(x.Team, TeamNames.ShortCode(x.Team), x.Opp, x.Runs!.Value, x.Wkts ?? 0, Overs(x.Balls ?? 0), x.m.Year, x.m.Date, x.m.Id);
            var highest = innings.OrderByDescending(x => x.Runs).Take(10).Select(T).ToList();
            // Lowest completed totals: all out, or the full overs used.
            var lowest = innings.Where(x => x.Wkts >= 10 || x.Balls >= (x.m.OversLimit ?? 20) * 6).OrderBy(x => x.Runs).Take(10).Select(T).ToList();
            var source = matches.Select(m => m.DataSource).FirstOrDefault() ?? "";
            return new RecordsDto(boards, highest, lowest, source);
        }, TimeSpan.FromMinutes(30), ct);
    }

    public async Task<HeadToHeadDto> HeadToHeadAsync(string code1, string code2, CancellationToken ct)
    {
        me.Require(Perm.StatsRead);
        code1 = code1.ToUpperInvariant(); code2 = code2.ToUpperInvariant();
        if (code1 == code2) throw new AppValidationException("team2", "Choose two different teams.");
        var names1 = TeamNames.FranchiseCodeByName.Where(kv => kv.Value == code1).Select(kv => kv.Key).ToList();
        var names2 = TeamNames.FranchiseCodeByName.Where(kv => kv.Value == code2).Select(kv => kv.Key).ToList();
        if (names1.Count == 0 || names2.Count == 0) throw new AppValidationException("team", "Unknown team code.");
        var matches = await db.Matches.AsNoTracking()
            .Where(m => m.Competition!.Code == Competition && m.ExternalRef != null
                && ((names1.Contains(m.Team1) && names2.Contains(m.Team2)) || (names2.Contains(m.Team1) && names1.Contains(m.Team2))))
            .OrderByDescending(m => m.Date).ToListAsync(ct);
        int w1 = matches.Count(m => m.Winner != null && names1.Contains(m.Winner));
        int w2 = matches.Count(m => m.Winner != null && names2.Contains(m.Winner));

        TeamRecordDto? Highest(List<string> names) => matches
            .Select(m => names.Contains(m.Team1) ? (m, Team: m.Team1, Opp: m.Team2, Runs: m.Team1Runs, Wk: m.Team1Wickets, B: m.Team1Balls)
                : (m, Team: m.Team2, Opp: m.Team1, Runs: m.Team2Runs, Wk: m.Team2Wickets, B: m.Team2Balls))
            .Where(x => x.Runs is not null).OrderByDescending(x => x.Runs)
            .Select(x => new TeamRecordDto(x.Team, TeamNames.ShortCode(x.Team), x.Opp, x.Runs!.Value, x.Wk ?? 0, Overs(x.B ?? 0), x.m.Year, x.m.Date, x.m.Id))
            .FirstOrDefault();

        // Top performers in these games, from the scorecards.
        var runs = new Dictionary<string, (string Name, string Team, int Runs, int Inns)>();
        var wkts = new Dictionary<string, (string Name, string Team, int Wkts, int Inns)>();
        foreach (var m in matches.Where(m => m.ScorecardJson != null))
        {
            using var doc = JsonDocument.Parse(m.ScorecardJson!);
            foreach (var inn in doc.RootElement.GetProperty("innings").EnumerateArray())
            {
                var team = inn.GetProperty("team").GetString()!;
                var other = team == m.Team1 ? m.Team2 : m.Team1;
                foreach (var b in inn.GetProperty("batting").EnumerateArray())
                {
                    var id = b.GetProperty("id").GetString()!;
                    var cur = runs.TryGetValue(id, out var r0) ? r0 : (Name: b.GetProperty("name").GetString()!, Team: team, Runs: 0, Inns: 0);
                    runs[id] = (cur.Name, team, cur.Runs + b.GetProperty("runs").GetInt32(), cur.Inns + 1);
                }
                foreach (var b in inn.GetProperty("bowling").EnumerateArray())
                {
                    var id = b.GetProperty("id").GetString()!;
                    var cur = wkts.TryGetValue(id, out var w0) ? w0 : (Name: b.GetProperty("name").GetString()!, Team: other, Wkts: 0, Inns: 0);
                    wkts[id] = (cur.Name, other, cur.Wkts + b.GetProperty("wickets").GetInt32(), cur.Inns + 1);
                }
            }
        }
        var linked = await LinkedAsync(runs.Keys.Concat(wkts.Keys), ct);
        var topRuns = runs.OrderByDescending(x => x.Value.Runs).Take(5).Select((x, i) => new LeaderRowDto(i + 1, x.Value.Name, linked.GetValueOrDefault(x.Key),
            x.Value.Team, TeamNames.ShortCode(x.Value.Team), $"{x.Value.Runs}", $"{x.Value.Inns} inns")).ToList();
        var topWkts = wkts.OrderByDescending(x => x.Value.Wkts).Take(5).Select((x, i) => new LeaderRowDto(i + 1, x.Value.Name, linked.GetValueOrDefault(x.Key),
            x.Value.Team, TeamNames.ShortCode(x.Value.Team), $"{x.Value.Wkts}", $"{x.Value.Inns} inns")).ToList();

        var fixtures = new List<FixtureDto>();
        foreach (var y in matches.Take(10).Select(m => m.Year).Distinct())
            fixtures.AddRange((await season.FixturesAsync(y, code1, ct)).Where(f => f.Team1.Team.Code == code2 || f.Team2.Team.Code == code2));
        var badge1 = fixtures.Select(f => f.Team1.Team.Code == code1 ? f.Team1.Team : f.Team2.Team).FirstOrDefault() ?? new TeamBadgeDto(names1[^1], code1, null, null, null);
        var badge2 = fixtures.Select(f => f.Team1.Team.Code == code2 ? f.Team1.Team : f.Team2.Team).FirstOrDefault() ?? new TeamBadgeDto(names2[^1], code2, null, null, null);
        return new HeadToHeadDto(badge1, badge2, matches.Count, w1, w2, matches.Count - w1 - w2, Highest(names1), Highest(names2),
            fixtures.OrderByDescending(f => f.Date).Take(10).ToList(), topRuns, topWkts, matches.Select(m => m.DataSource).FirstOrDefault() ?? "");
    }

    public async Task<MatchupDto?> MatchupAsync(Guid batterId, Guid bowlerId, CancellationToken ct)
    {
        me.Require(Perm.StatsRead);
        var ids = await db.Players.AsNoTracking().Where(p => p.Id == batterId || p.Id == bowlerId).ToDictionaryAsync(p => p.Id, ct);
        if (!ids.TryGetValue(batterId, out var bat) || !ids.TryGetValue(bowlerId, out var bowl)) throw new NotFoundException("Player");
        if (bat.CricsheetId is null || bowl.CricsheetId is null) return null;
        var m = await db.BatterBowlerMatchups.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Competition!.Code == Competition && x.BatterId == bat.CricsheetId && x.BowlerId == bowl.CricsheetId, ct);
        return m is null ? new MatchupDto(bat.FullName, bat.Id, bowl.FullName, bowl.Id, 0, 0, 0, 0, 0, 0, null, null, null) : Dto(m, bat.Id, bowl.Id, bat.FullName, bowl.FullName);
    }

    public async Task<PlayerMatchupsDto> PlayerMatchupsAsync(Guid playerId, CancellationToken ct)
    {
        me.Require(Perm.StatsRead);
        var p = await db.Players.AsNoTracking().FirstOrDefaultAsync(x => x.Id == playerId, ct) ?? throw new NotFoundException("Player");
        if (p.CricsheetId is null) return new PlayerMatchupsDto(p.Id, p.FullName, [], [], [], [], "");
        var asBat = await db.BatterBowlerMatchups.AsNoTracking()
            .Where(x => x.Competition!.Code == Competition && x.BatterId == p.CricsheetId && x.Balls >= MinMatchupBalls).ToListAsync(ct);
        var asBowl = await db.BatterBowlerMatchups.AsNoTracking()
            .Where(x => x.Competition!.Code == Competition && x.BowlerId == p.CricsheetId && x.Balls >= MinMatchupBalls).ToListAsync(ct);
        var linked = await LinkedAsync(asBat.Select(x => x.BowlerId).Concat(asBowl.Select(x => x.BatterId)), ct);
        MatchupDto D(BatterBowlerMatchup x) => Dto(x, linked.GetValueOrDefault(x.BatterId), linked.GetValueOrDefault(x.BowlerId), x.BatterName, x.BowlerName);
        return new PlayerMatchupsDto(p.Id, p.FullName,
            asBat.OrderByDescending(x => x.Dismissals).ThenBy(x => (decimal)x.Runs / x.Balls).Take(6).Select(D).ToList(),
            asBat.OrderByDescending(x => x.Runs).ThenByDescending(x => (decimal)x.Runs / x.Balls).Take(6).Select(D).ToList(),
            asBowl.OrderByDescending(x => x.Dismissals).ThenBy(x => (decimal)x.Runs / x.Balls).Take(6).Select(D).ToList(),
            asBowl.OrderByDescending(x => (decimal)x.Runs / x.Balls).ThenByDescending(x => x.Runs).Take(6).Select(D).ToList(),
            "Cricsheet (cricsheet.org), ODC-BY 1.0 · IPL only · at least " + MinMatchupBalls + " balls");
    }

    // ------------------------------------------------------------------

    private async Task<List<LeaderBoardDto>> BoardsAsync(List<Agg> rows, CancellationToken ct, bool includeHundreds = false)
    {
        var linked = await LinkedAsync(rows.Select(r => r.Id), ct);
        LeaderBoardDto Board(string key, string title, string icon, IEnumerable<Agg> ordered, Func<Agg, string> value, Func<Agg, string> detail) =>
            new(key, title, icon, ordered.Take(10).Select((r, i) => new LeaderRowDto(i + 1, r.Name, linked.GetValueOrDefault(r.Id), r.Team, TeamNames.ShortCode(r.Team), value(r), detail(r))).ToList());
        var list = new List<LeaderBoardDto>
        {
            Board("runs", "Most runs", "sports_cricket", rows.Where(r => r.Runs > 0).OrderByDescending(r => r.Runs).ThenByDescending(r => SR(r)),
                r => r.Runs.ToString(), r => $"{r.Innings} inns · avg {Fmt(r.Innings - r.NotOuts > 0 ? (decimal)r.Runs / (r.Innings - r.NotOuts) : null)} · SR {Fmt(SR(r))}"),
            Board("wickets", "Most wickets", "sports_baseball", rows.Where(r => r.Wickets > 0).OrderByDescending(r => r.Wickets).ThenBy(r => Econ(r)),
                r => r.Wickets.ToString(), r => $"econ {Fmt(Econ(r))} · best {r.BestWickets}/{r.BestRuns}"),
            Board("sixes", "Most sixes", "rocket_launch", rows.Where(r => r.Sixes > 0).OrderByDescending(r => r.Sixes), r => r.Sixes.ToString(), r => $"{r.Fours} fours"),
            Board("highest", "Highest score", "trending_up", rows.Where(r => r.HighestScore > 0).OrderByDescending(r => r.HighestScore).ThenByDescending(r => r.HighestNotOut),
                r => r.HighestScore + (r.HighestNotOut ? "*" : ""), r => r.Year is { } y ? y.ToString() : ""),
            Board("best-bowling", "Best bowling", "local_fire_department", rows.Where(r => r.BestWickets > 0).OrderByDescending(r => r.BestWickets).ThenBy(r => r.BestRuns),
                r => $"{r.BestWickets}/{r.BestRuns}", r => r.BestYear is { } y ? y.ToString() : ""),
            Board("strike-rate", $"Best strike rate (min {MinBallsForRate} balls)", "speed", rows.Where(r => r.Balls >= MinBallsForRate).OrderByDescending(SR),
                r => Fmt(SR(r)), r => $"{r.Runs} runs off {r.Balls}"),
            Board("economy", $"Best economy (min {MinBallsForEconomy} balls)", "savings", rows.Where(r => r.BallsBowled >= MinBallsForEconomy).OrderBy(Econ),
                r => Fmt(Econ(r)), r => $"{r.Wickets} wkts in {Overs(r.BallsBowled)} ov"),
            Board("catches", "Most catches", "back_hand", rows.Where(r => r.Catches > 0).OrderByDescending(r => r.Catches), r => r.Catches.ToString(), r => $"{r.Matches} matches"),
        };
        if (includeHundreds)
            list.Insert(3, Board("hundreds", "Most hundreds", "military_tech", rows.Where(r => r.Hundreds > 0).OrderByDescending(r => r.Hundreds).ThenByDescending(r => r.Fifties),
                r => r.Hundreds.ToString(), r => $"{r.Fifties} fifties"));
        return list;
    }

    private async Task<Dictionary<string, Guid>> LinkedAsync(IEnumerable<string> personIds, CancellationToken ct)
    {
        var ids = personIds.Distinct().ToList();
        return await db.Players.AsNoTracking().Where(p => p.CricsheetId != null && ids.Contains(p.CricsheetId))
            .ToDictionaryAsync(p => p.CricsheetId!, p => p.Id, ct);
    }

    private static MatchupDto Dto(BatterBowlerMatchup m, Guid? batter, Guid? bowler, string batterName, string bowlerName) =>
        new(batterName, batter, bowlerName, bowler, m.Balls, m.Runs, m.Dismissals, m.Fours, m.Sixes, m.Dots,
            m.Balls > 0 ? Math.Round(m.Runs * 100m / m.Balls, 2) : null, m.Dismissals > 0 ? Math.Round((decimal)m.Runs / m.Dismissals, 2) : null,
            m.Balls > 0 ? Math.Round(m.Dots * 100m / m.Balls, 1) : null);

    private static decimal? SR(Agg r) => r.Balls > 0 ? r.Runs * 100m / r.Balls : null;
    private static decimal? Econ(Agg r) => r.BallsBowled > 0 ? r.RunsConceded * 6m / r.BallsBowled : null;
    private static string Fmt(decimal? v) => v is null ? "—" : Math.Round(v.Value, 2).ToString("0.00");
    private static string Overs(int balls) => $"{balls / 6}{(balls % 6 > 0 ? "." + balls % 6 : "")}";

    private sealed record Agg(string Id, string Name, string Team, int Matches, int Innings, int NotOuts, int Runs, int Balls, int HighestScore, bool HighestNotOut,
        int? Year, int Fifties, int Hundreds, int Fours, int Sixes, int BallsBowled, int RunsConceded, int Wickets, int BestWickets, int BestRuns, int? BestYear, int Catches)
    {
        public static Agg From(PersonSeasonStat s) => new(s.PersonId, s.Name, s.Team, s.Matches, s.Innings, s.NotOuts, s.Runs, s.Balls, s.HighestScore, s.HighestNotOut,
            s.Year, s.Fifties, s.Hundreds, s.Fours, s.Sixes, s.BallsBowled, s.RunsConceded, s.Wickets, s.BestWickets, s.BestRuns, s.Year, s.Catches);

        public static Agg Sum(IReadOnlyList<PersonSeasonStat> g)
        {
            var hs = g.OrderByDescending(x => x.HighestScore).ThenByDescending(x => x.HighestNotOut).First();
            var bb = g.Where(x => x.BestWickets > 0).OrderByDescending(x => x.BestWickets).ThenBy(x => x.BestRuns).FirstOrDefault();
            var last = g[^1];
            return new(last.PersonId, last.Name, last.Team, g.Sum(x => x.Matches), g.Sum(x => x.Innings), g.Sum(x => x.NotOuts), g.Sum(x => x.Runs), g.Sum(x => x.Balls),
                hs.HighestScore, hs.HighestNotOut, hs.Year, g.Sum(x => x.Fifties), g.Sum(x => x.Hundreds), g.Sum(x => x.Fours), g.Sum(x => x.Sixes),
                g.Sum(x => x.BallsBowled), g.Sum(x => x.RunsConceded), g.Sum(x => x.Wickets), bb?.BestWickets ?? 0, bb?.BestRuns ?? 0, bb?.Year, g.Sum(x => x.Catches));
        }
    }
}
