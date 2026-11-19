using System.Text.Json;
using Ipl.Domain;
using Ipl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Infrastructure.Services;

/// <summary>One person's numbers in one match (everyone who played, not only linked squad players).</summary>
public sealed class PersonMatchLine(string id, string name, string team)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Team { get; } = team;
    public bool Batted, Out;
    public int Runs, Balls, Fours, Sixes, BallsBowled, RunsConceded, Wickets, Catches, Dots;
    public bool Bowled => BallsBowled > 0 || RunsConceded > 0;
}

public sealed class MatchupLine
{
    public string BatterName = "", BowlerName = "";
    public int Balls, Runs, Dismissals, Fours, Sixes, Dots;
}

public sealed record ScorecardResult(string Json, int Year, IReadOnlyList<PersonMatchLine> People, IReadOnlyDictionary<(string Bat, string Bowl), MatchupLine> Matchups);

/// <summary>Builds a full scorecard (batting with dismissals, bowling, extras, fall of wickets) from one Cricsheet match.</summary>
public static class CricsheetScorecard
{
    private static readonly HashSet<string> NotBowlerWickets = new(StringComparer.OrdinalIgnoreCase)
    {
        "run out", "retired hurt", "retired out", "retired not out", "obstructing the field", "handled the ball", "timed out",
    };
    private static readonly HashSet<string> NotDismissals = new(StringComparer.OrdinalIgnoreCase) { "retired hurt", "retired not out" };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class Bat { public string Id = "", Name = ""; public int Runs, Balls, Fours, Sixes; public string How = "not out"; }
    private sealed class Bowl { public string Id = "", Name = ""; public int Balls, Runs, Wickets, Maidens, Wides, NoBalls, Dots; }

    public static string Overs(int balls) => $"{balls / 6}{(balls % 6 > 0 ? "." + balls % 6 : "")}";

    public static ScorecardResult Build(JsonElement root)
    {
        var info = root.GetProperty("info");
        var registry = CricsheetMatchParser.Registry(info);
        string Id(string name) => registry.TryGetValue(name, out var id) ? id : "name:" + name;
        var teams = info.GetProperty("teams").EnumerateArray().Select(t => t.GetString()!).ToArray();
        string Other(string team) => teams.FirstOrDefault(t => t != team) ?? "";
        var squads = new Dictionary<string, List<string>>();
        if (info.TryGetProperty("players", out var pl))
            foreach (var t in pl.EnumerateObject()) squads[t.Name] = t.Value.EnumerateArray().Select(n => n.GetString()!).ToList();

        var people = new Dictionary<string, PersonMatchLine>();
        PersonMatchLine Person(string name, string team)
        {
            var id = Id(name);
            if (!people.TryGetValue(id, out var p)) people[id] = p = new PersonMatchLine(id, name, team);
            return p;
        }
        foreach (var (team, names) in squads) foreach (var n in names) Person(n, team);

        var matchups = new Dictionary<(string, string), MatchupLine>();
        var inningsOut = new List<object>();
        if (root.TryGetProperty("innings", out var innings))
            foreach (var inn in innings.EnumerateArray())
            {
                if (inn.TryGetProperty("super_over", out var so) && so.ValueKind == JsonValueKind.True) continue;
                if (!inn.TryGetProperty("overs", out var overs)) continue;
                var batting = inn.GetProperty("team").GetString()!;
                var fielding = Other(batting);
                var bats = new List<Bat>();
                var bowls = new List<Bowl>();
                Bat B(string n) { var b = bats.FirstOrDefault(x => x.Name == n); if (b is null) bats.Add(b = new Bat { Id = Id(n), Name = n }); return b; }
                Bowl W(string n) { var w = bowls.FirstOrDefault(x => x.Name == n); if (w is null) bowls.Add(w = new Bowl { Id = Id(n), Name = n }); return w; }
                int total = 0, wickets = 0, legal = 0, byes = 0, legByes = 0, wides = 0, noBalls = 0, penalty = 0;
                var fow = new List<string>();

                foreach (var over in overs.EnumerateArray())
                {
                    var byBowler = new Dictionary<string, (int Legal, int Conceded)>();
                    foreach (var d in over.GetProperty("deliveries").EnumerateArray())
                    {
                        var batter = d.GetProperty("batter").GetString()!;
                        var bowler = d.GetProperty("bowler").GetString()!;
                        var b = B(batter);
                        if (d.TryGetProperty("non_striker", out var ns)) B(ns.GetString()!);
                        var runs = d.GetProperty("runs");
                        var rb = runs.GetProperty("batter").GetInt32();
                        int w = 0, nb = 0;
                        if (d.TryGetProperty("extras", out var ex))
                        {
                            if (ex.TryGetProperty("wides", out var x1)) { w = x1.GetInt32(); wides += w; }
                            if (ex.TryGetProperty("noballs", out var x2)) { nb = x2.GetInt32(); noBalls += nb; }
                            if (ex.TryGetProperty("byes", out var x3)) byes += x3.GetInt32();
                            if (ex.TryGetProperty("legbyes", out var x4)) legByes += x4.GetInt32();
                            if (ex.TryGetProperty("penalty", out var x5)) penalty += x5.GetInt32();
                        }
                        var isLegal = w == 0 && nb == 0;
                        var boundary = !(d.TryGetProperty("non_boundary", out var nbd) && nbd.ValueKind == JsonValueKind.True);
                        total += runs.GetProperty("total").GetInt32();
                        if (isLegal) legal++;

                        var key = (Id(batter), Id(bowler));
                        if (!matchups.TryGetValue(key, out var mu)) matchups[key] = mu = new MatchupLine { BatterName = batter, BowlerName = bowler };
                        if (w == 0)
                        {
                            b.Balls++; b.Runs += rb;
                            if (boundary && rb == 4) b.Fours++;
                            if (boundary && rb == 6) b.Sixes++;
                            mu.Balls++; mu.Runs += rb;
                            if (boundary && rb == 4) mu.Fours++;
                            if (boundary && rb == 6) mu.Sixes++;
                            if (rb == 0) mu.Dots++;
                        }
                        var bw = W(bowler);
                        var conceded = rb + w + nb;
                        bw.Runs += conceded; bw.Wides += w; bw.NoBalls += nb;
                        if (isLegal) bw.Balls++;
                        if (isLegal && conceded == 0) bw.Dots++;
                        byBowler.TryGetValue(bowler, out var ob);
                        byBowler[bowler] = (ob.Legal + (isLegal ? 1 : 0), ob.Conceded + conceded);

                        if (!d.TryGetProperty("wickets", out var wk)) continue;
                        foreach (var x in wk.EnumerateArray())
                        {
                            var kind = x.GetProperty("kind").GetString() ?? "";
                            var outName = x.TryGetProperty("player_out", out var po) ? po.GetString() : null;
                            var fielder = x.TryGetProperty("fielders", out var fs) && fs.GetArrayLength() > 0 && fs[0].TryGetProperty("name", out var fn) ? fn.GetString() : null;
                            var credit = !NotBowlerWickets.Contains(kind);
                            if (credit) bw.Wickets++;
                            if (outName is null) continue;
                            var victim = B(outName);
                            victim.How = kind switch
                            {
                                "bowled" => $"b {bowler}",
                                "caught" => $"c {fielder ?? "sub"} b {bowler}",
                                "caught and bowled" => $"c & b {bowler}",
                                "lbw" => $"lbw b {bowler}",
                                "stumped" => $"st {fielder} b {bowler}",
                                "run out" => fielder is null ? "run out" : $"run out ({fielder})",
                                "hit wicket" => $"hit wicket b {bowler}",
                                _ => kind,
                            };
                            if (kind == "caught" && fielder is not null) Person(fielder, fielding).Catches++;
                            if (kind == "caught and bowled") Person(bowler, fielding).Catches++;
                            if (credit && outName == batter) mu.Dismissals++;
                            if (!NotDismissals.Contains(kind))
                            {
                                wickets++;
                                fow.Add($"{wickets}-{total} ({outName}, {Overs(legal)} ov)");
                            }
                        }
                    }
                    if (byBowler.Count == 1)
                    {
                        var (name, (lg, c)) = byBowler.First();
                        if (lg >= 6 && c == 0) W(name).Maidens++;
                    }
                }

                foreach (var b in bats)
                {
                    var p = Person(b.Name, batting);
                    p.Batted = true; p.Runs += b.Runs; p.Balls += b.Balls; p.Fours += b.Fours; p.Sixes += b.Sixes;
                    if (b.How is not ("not out" or "retired hurt" or "retired not out")) p.Out = true;
                }
                foreach (var w in bowls)
                {
                    var p = Person(w.Name, fielding);
                    p.BallsBowled += w.Balls; p.RunsConceded += w.Runs; p.Wickets += w.Wickets; p.Dots += w.Dots;
                }
                var didNotBat = (squads.GetValueOrDefault(batting) ?? []).Where(n => bats.All(b => b.Name != n)).ToList();
                inningsOut.Add(new
                {
                    team = batting, runs = total, wickets, overs = Overs(legal),
                    extras = new { total = byes + legByes + wides + noBalls + penalty, byes, legByes, wides, noBalls, penalty },
                    batting = bats.Select(b => new { id = b.Id, name = b.Name, runs = b.Runs, balls = b.Balls, fours = b.Fours, sixes = b.Sixes, how = b.How,
                        strikeRate = b.Balls > 0 ? Math.Round(b.Runs * 100m / b.Balls, 2) : (decimal?)null }),
                    didNotBat,
                    bowling = bowls.Select(w => new { id = w.Id, name = w.Name, overs = Overs(w.Balls), maidens = w.Maidens, runs = w.Runs, wickets = w.Wickets,
                        economy = w.Balls > 0 ? Math.Round(w.Runs * 6m / w.Balls, 2) : (decimal?)null, wides = w.Wides, noBalls = w.NoBalls, dots = w.Dots }),
                    fallOfWickets = fow,
                });
            }

        var pom = info.TryGetProperty("player_of_match", out var pm) ? pm.EnumerateArray().Select(x => x.GetString()).ToList() : [];
        var toss = info.TryGetProperty("toss", out var ts) && ts.TryGetProperty("winner", out var tw) && ts.TryGetProperty("decision", out var td)
            ? $"{tw.GetString()} won the toss and chose to {td.GetString()}" : null;
        var json = JsonSerializer.Serialize(new { innings = inningsOut, playerOfMatch = pom, toss }, Json);
        return new ScorecardResult(json, CricsheetMatchParser.FirstDate(info).Year, people.Values.ToList(), matchups);
    }
}

/// <summary>Rebuilds IPL scorecards, every person's season totals and batter-v-bowler records from the full archive.</summary>
public static class CricsheetArchive
{
    public static async Task<(int Scorecards, int People, int Matchups)> RebuildAsync(AppDbContext db, IEnumerable<(string FileId, JsonDocument Doc)> entries, int competitionId, CancellationToken ct)
    {
        var missingCards = await db.Matches.Where(m => m.CompetitionId == competitionId && m.ExternalRef != null && m.ScorecardJson == null)
            .ToDictionaryAsync(m => m.ExternalRef!, ct);
        var seasons = new Dictionary<(int Year, string Id), PersonSeasonStat>();
        var allMatchups = new Dictionary<(string, string), MatchupLine>();
        var cards = 0;
        foreach (var (fileId, doc) in entries)
            using (doc)
            {
                var info = doc.RootElement.GetProperty("info");
                if (!CricsheetMatchParser.IsMaleMatch(info)) continue;
                var sc = CricsheetScorecard.Build(doc.RootElement);
                if (missingCards.TryGetValue(Cricsheet.RefPrefix + fileId, out var match))
                {
                    match.ScorecardJson = sc.Json;
                    cards++;
                }
                foreach (var p in sc.People)
                {
                    if (p.Id.StartsWith("name:")) continue;
                    if (!seasons.TryGetValue((sc.Year, p.Id), out var s))
                        seasons[(sc.Year, p.Id)] = s = new PersonSeasonStat { CompetitionId = competitionId, Year = sc.Year, PersonId = p.Id };
                    s.Name = Cut(p.Name, 100);
                    s.Team = Cut(p.Team, 100);
                    s.Matches++;
                    if (p.Batted)
                    {
                        s.Innings++;
                        if (!p.Out) s.NotOuts++;
                        s.Runs += p.Runs; s.Balls += p.Balls; s.Fours += p.Fours; s.Sixes += p.Sixes;
                        if (p.Runs >= 100) s.Hundreds++; else if (p.Runs >= 50) s.Fifties++;
                        if (p.Runs > s.HighestScore || (p.Runs == s.HighestScore && !p.Out)) { s.HighestScore = p.Runs; s.HighestNotOut = !p.Out; }
                    }
                    if (p.Bowled)
                    {
                        s.BallsBowled += p.BallsBowled; s.RunsConceded += p.RunsConceded; s.Wickets += p.Wickets; s.DotBalls += p.Dots;
                        if (p.Wickets > s.BestWickets || (p.Wickets == s.BestWickets && (s.BestRuns == 0 && s.BestWickets == 0 || p.RunsConceded < s.BestRuns)))
                        { s.BestWickets = p.Wickets; s.BestRuns = p.RunsConceded; }
                    }
                    s.Catches += p.Catches;
                }
                foreach (var (key, m) in sc.Matchups)
                {
                    if (key.Bat.StartsWith("name:") || key.Bowl.StartsWith("name:")) continue;
                    if (!allMatchups.TryGetValue(key, out var a)) allMatchups[key] = a = new MatchupLine { BatterName = m.BatterName, BowlerName = m.BowlerName };
                    a.BatterName = m.BatterName; a.BowlerName = m.BowlerName;
                    a.Balls += m.Balls; a.Runs += m.Runs; a.Dismissals += m.Dismissals; a.Fours += m.Fours; a.Sixes += m.Sixes; a.Dots += m.Dots;
                }
            }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct); // scorecards
            await db.PersonSeasonStats.Where(s => s.CompetitionId == competitionId).ExecuteDeleteAsync(ct);
            await db.BatterBowlerMatchups.Where(s => s.CompetitionId == competitionId).ExecuteDeleteAsync(ct);
            await DemoSeeder.BulkInsertAsync(db, seasons.Values.ToList(), "PersonSeasonStats", ct);
            await DemoSeeder.BulkInsertAsync(db, allMatchups.Select(kv => new BatterBowlerMatchup
            {
                CompetitionId = competitionId, BatterId = kv.Key.Item1, BowlerId = kv.Key.Item2, BatterName = Cut(kv.Value.BatterName, 100),
                BowlerName = Cut(kv.Value.BowlerName, 100), Balls = kv.Value.Balls, Runs = kv.Value.Runs, Dismissals = kv.Value.Dismissals,
                Fours = kv.Value.Fours, Sixes = kv.Value.Sixes, Dots = kv.Value.Dots,
            }).ToList(), "BatterBowlerMatchups", ct);
            await tx.CommitAsync(ct);
        });
        return (cards, seasons.Count, allMatchups.Count);
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}
