using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Ipl.Application;
using Ipl.Application.Statistics;
using Ipl.Domain;
using Ipl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ipl.Infrastructure.Services;

/// <summary>
/// Ball-by-ball data from Cricsheet (https://cricsheet.org), published under the Open Data Commons Attribution
/// License (ODC-BY 1.0). Attribution is shown with every statistic through <see cref="Cricsheet.Source"/>.
/// </summary>
public class CricsheetOptions
{
    public string BaseUrl { get; set; } = "https://cricsheet.org/downloads/";
    /// <summary>Archives to load: "ipl" (IPL) and "t20s_male" (men's T20 internationals).</summary>
    public string[] Datasets { get; set; } = ["ipl", "t20s_male"];
    /// <summary>Re-import automatically every N days (0 = only when an administrator starts it).</summary>
    public int AutoImportDays { get; set; }
    /// <summary>Optional folder with already-downloaded "{dataset}_json.zip" files (for servers without internet access).</summary>
    public string? LocalFolder { get; set; }
    /// <summary>Download free player photos (Wikimedia Commons) after every successful import.</summary>
    public bool RefreshPhotosAfterImport { get; set; } = true;
}

public static class Cricsheet
{
    public const string Source = "Cricsheet (cricsheet.org), ODC-BY 1.0";
    public const string RefPrefix = "cricsheet:";
    public const string LastRunKey = "cricsheet.lastRun";

    public static readonly IReadOnlyDictionary<string, string> CompetitionByDataset = new Dictionary<string, string>
    {
        ["ipl"] = "IPL", ["t20s_male"] = "T20I", ["t20s"] = "T20I",
    };

    /// <summary>Historical and current IPL team names → current franchise code.</summary>
    public static IReadOnlyDictionary<string, string> FranchiseByTeamName => TeamNames.FranchiseCodeByName;

    /// <summary>Home cities per franchise (used for the home/away split of IPL matches).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> HomeCities = new Dictionary<string, string[]>
    {
        ["CSK"] = ["Chennai"], ["MI"] = ["Mumbai"], ["RCB"] = ["Bangalore", "Bengaluru"], ["KKR"] = ["Kolkata"],
        ["DC"] = ["Delhi"], ["RR"] = ["Jaipur", "Guwahati"], ["PBKS"] = ["Chandigarh", "Mohali", "Mullanpur", "Dharamsala", "Dharmasala"],
        ["SRH"] = ["Hyderabad"], ["LSG"] = ["Lucknow"], ["GT"] = ["Ahmedabad"],
    };
}

/// <summary>A person seen in the Cricsheet data: names used and the last year per current franchise (or "T20I:{country}").</summary>
public sealed class CricsheetPerson(string id)
{
    public string Id { get; } = id;
    public HashSet<string> Names { get; } = [];
    public Dictionary<string, int> FranchiseYears { get; } = [];
    public int LastYear { get; set; }
}

/// <summary>A squad player to link; CountryTeam is the national team name for overseas players (matched through T20Is).</summary>
public sealed record SquadPlayerRef(Guid Id, string FullName, string? FranchiseCode, string? CountryTeam = null);

/// <summary>
/// Links squad players to Cricsheet people. Cricsheet uses scorecard names ("V Kohli", "SA Yadav"), so a match needs
/// the name to fit (surname and initials, small spelling differences) AND the person to have played for the player's
/// current franchise in the last three IPL seasons. Exact full-name matches of recent players are accepted on their own.
/// Anything ambiguous is left unlinked: a wrong link is worse than none.
/// </summary>
public static class CricsheetNameMatcher
{
    public static Dictionary<Guid, string> Match(IReadOnlyCollection<SquadPlayerRef> players, IReadOnlyCollection<CricsheetPerson> people, int latestYear)
    {
        var result = new Dictionary<Guid, string>();
        var claimed = new HashSet<string>();
        foreach (var p in players)
        {
            var fitting = people.Where(x => x.Names.Any(n => NameFits(n, p.FullName))).ToList();
            var exact = fitting.Where(x => x.Names.Any(n => Same(n, p.FullName))).ToList();
            List<CricsheetPerson> pick = [];
            if (p.FranchiseCode is { } code)
            {
                pick = fitting.Where(x => x.FranchiseYears.TryGetValue(code, out var y) && y >= latestYear - 2).ToList();
                if (pick.Count > 1) pick = pick.Where(exact.Contains).ToList();
            }
            if (pick.Count == 0 && p.CountryTeam is { } country)
            {
                pick = fitting.Where(x => x.FranchiseYears.TryGetValue("T20I:" + country, out var y) && y >= latestYear - 3).ToList();
                if (pick.Count > 1) pick = pick.Where(exact.Contains).ToList();
            }
            if (pick.Count == 0)
            {
                var recentExact = exact.Where(x => x.LastYear >= latestYear - 5).ToList();
                if (recentExact.Count == 1) pick = recentExact;
            }
            if (pick.Count == 1 && claimed.Add(pick[0].Id)) result[p.Id] = pick[0].Id;
        }
        return result;
    }

    private static string[] Tokens(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch is '.' or '\'' or '’' or '-' or '(' or ')' ? ' ' : ch);
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !t.All(char.IsDigit)).ToArray();
    }

    private static bool Same(string a, string b) =>
        string.Join(' ', Tokens(a)).Equals(string.Join(' ', Tokens(b)), StringComparison.OrdinalIgnoreCase);

    private static bool IsInitials(string t) => t.Length <= 4 && t.All(char.IsUpper);

    private static bool Fits(string w, string x) =>
        w == x || (w.Length >= 7 && Levenshtein(w, x) <= 2) || (w.Length >= 5 && x.Length > w.Length && x.EndsWith(w, StringComparison.Ordinal));

    /// <summary>Does the Cricsheet scorecard name fit the full name? E.g. "SA Yadav" fits "Suryakumar Yadav".</summary>
    public static bool NameFits(string cricsheetName, string fullName)
    {
        var full = Tokens(fullName).Select(t => t.ToLowerInvariant()).ToArray();
        var toks = Tokens(cricsheetName);
        var words = toks.Where(t => !IsInitials(t) || full.Contains(t.ToLowerInvariant())).Select(t => t.ToLowerInvariant()).ToList();
        var initials = string.Concat(toks.Where(t => IsInitials(t) && !full.Contains(t.ToLowerInvariant()))).ToLowerInvariant();
        if (words.Count == 0 || !words.All(w => full.Any(x => Fits(w, x)))) return false;
        if (initials.Length == 0 || words.Count >= 2) return true;
        var rest = full.Where(x => !words.Any(w => Fits(w, x))).ToList();
        return initials.Any(ch => rest.Any(r => r[0] == ch));
    }

    private static int Levenshtein(string a, string b)
    {
        var d = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = d[0];
            d[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var t = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = t;
            }
        }
        return d[b.Length];
    }
}

/// <summary>What the importer knows about a linked player (for pace/spin and batting-hand splits).</summary>
public sealed record LinkedPlayer(Guid PlayerId, BattingHand Hand, BowlingType Bowling);

public sealed record InningsTotal(string Team, int Runs, int Wickets, int Balls);

public sealed record MatchSummary(int? MatchNumber, string? Stage, string? Winner, bool NoResult, int? OversLimit, int? TargetOvers,
    IReadOnlyList<InningsTotal> Innings);

public sealed record ParsedMatch(
    string FileId, DateOnly Date, string Team1, string Team2, string Venue, string? City, string? Result,
    IReadOnlyDictionary<string, PlayerMatchStatistic> Performances, MatchSummary Summary);

/// <summary>Turns one Cricsheet JSON match into per-player match statistics (keyed by Cricsheet id).</summary>
public static class CricsheetMatchParser
{
    private static readonly HashSet<string> NotBowlerWickets = new(StringComparer.OrdinalIgnoreCase)
    {
        "run out", "retired hurt", "retired out", "retired not out", "obstructing the field", "handled the ball", "timed out",
    };

    private static readonly HashSet<string> NotDismissals = new(StringComparer.OrdinalIgnoreCase) { "retired hurt", "retired not out" };

    public static bool IsMaleMatch(JsonElement info) =>
        !info.TryGetProperty("gender", out var g) || g.GetString() == "male";

    public static DateOnly FirstDate(JsonElement info) =>
        DateOnly.ParseExact(info.GetProperty("dates")[0].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static Dictionary<string, string> Registry(JsonElement info)
    {
        var map = new Dictionary<string, string>();
        if (info.TryGetProperty("registry", out var reg) && reg.TryGetProperty("people", out var people))
            foreach (var p in people.EnumerateObject()) map[p.Name] = p.Value.GetString()!;
        return map;
    }

    public static ParsedMatch Parse(string fileId, JsonElement root, Func<string, LinkedPlayer?> linked, bool homeAwayForIpl)
    {
        var info = root.GetProperty("info");
        var registry = Registry(info);
        string Id(string name) => registry.TryGetValue(name, out var id) ? id : "name:" + name;
        var teams = info.GetProperty("teams").EnumerateArray().Select(t => t.GetString()!).ToArray();
        var teamOf = new Dictionary<string, string>();
        if (info.TryGetProperty("players", out var squads))
            foreach (var team in squads.EnumerateObject())
                foreach (var n in team.Value.EnumerateArray())
                    teamOf[Id(n.GetString()!)] = team.Name;

        var perf = new Dictionary<string, PlayerMatchStatistic>();
        var totals = new List<InningsTotal>();
        int? targetOvers = null;
        PlayerMatchStatistic P(string id, string team)
        {
            if (!perf.TryGetValue(id, out var s))
            {
                perf[id] = s = new PlayerMatchStatistic();
                teamOf.TryAdd(id, team);
            }
            return s;
        }
        string Other(string team) => teams.FirstOrDefault(t => t != team) ?? "";
        BowlingType TypeOf(string id) => linked(id)?.Bowling ?? BowlingType.Unknown;

        if (root.TryGetProperty("innings", out var innings))
            foreach (var inn in innings.EnumerateArray())
            {
                if (inn.TryGetProperty("super_over", out var so) && so.ValueKind == JsonValueKind.True) continue;
                if (!inn.TryGetProperty("overs", out var overs)) continue;
                var battingTeam = inn.GetProperty("team").GetString()!;
                var fieldingTeam = Other(battingTeam);
                var order = new List<string>();
                int innRuns = 0, innWickets = 0, innBalls = 0;
                if (inn.TryGetProperty("target", out var target) && target.TryGetProperty("overs", out var to) && to.ValueKind == JsonValueKind.Number)
                    targetOvers = (int)Math.Ceiling(to.GetDouble());
                void Arrive(string id) { if (!order.Contains(id)) order.Add(id); }

                foreach (var over in overs.EnumerateArray())
                {
                    var o = over.GetProperty("over").GetInt32();
                    var phase = o < 6 ? 0 : o < 15 ? 1 : 2;
                    var byBowler = new Dictionary<string, (int Legal, int Conceded)>();
                    foreach (var d in over.GetProperty("deliveries").EnumerateArray())
                    {
                        var batter = Id(d.GetProperty("batter").GetString()!);
                        var bowler = Id(d.GetProperty("bowler").GetString()!);
                        Arrive(batter);
                        if (d.TryGetProperty("non_striker", out var ns)) Arrive(Id(ns.GetString()!));
                        var runs = d.GetProperty("runs");
                        var rb = runs.GetProperty("batter").GetInt32();
                        int wides = 0, noballs = 0;
                        if (d.TryGetProperty("extras", out var ex))
                        {
                            if (ex.TryGetProperty("wides", out var w)) wides = w.GetInt32();
                            if (ex.TryGetProperty("noballs", out var nb)) noballs = nb.GetInt32();
                        }
                        var legal = wides == 0 && noballs == 0;
                        innRuns += runs.GetProperty("total").GetInt32();
                        if (legal) innBalls++;
                        var boundary = !(d.TryGetProperty("non_boundary", out var nbd) && nbd.ValueKind == JsonValueKind.True);
                        var type = TypeOf(bowler);
                        var pace = type is BowlingType.Fast or BowlingType.MediumFast or BowlingType.Medium;
                        var spin = type is BowlingType.OffSpin or BowlingType.LegSpin or BowlingType.LeftArmOrthodox or BowlingType.LeftArmWristSpin;

                        var b = P(batter, battingTeam);
                        if (wides == 0)
                        {
                            b.BallsFaced++;
                            b.Runs += rb;
                            if (boundary && rb == 4) b.Fours++;
                            if (boundary && rb == 6) b.Sixes++;
                            switch (phase)
                            {
                                case 0: b.PowerplayRuns += rb; b.PowerplayBalls++; break;
                                case 1: b.MiddleRuns += rb; b.MiddleBalls++; break;
                                default: b.DeathRuns += rb; b.DeathBalls++; break;
                            }
                            if (pace) { b.RunsVsPace += rb; b.BallsVsPace++; }
                            else if (spin) { b.RunsVsSpin += rb; b.BallsVsSpin++; }
                        }

                        var bw = P(bowler, fieldingTeam);
                        bw.Bowled = true;
                        var conceded = rb + wides + noballs;
                        bw.RunsConceded += conceded;
                        if (legal) bw.BallsBowled++;
                        if (legal && conceded == 0) bw.DotBalls++;
                        switch (phase)
                        {
                            case 0: bw.PowerplayRunsConceded += conceded; if (legal) bw.PowerplayBallsBowled++; break;
                            case 1: bw.MiddleRunsConceded += conceded; if (legal) bw.MiddleBallsBowled++; break;
                            default: bw.DeathRunsConceded += conceded; if (legal) bw.DeathBallsBowled++; break;
                        }
                        byBowler.TryGetValue(bowler, out var ob);
                        byBowler[bowler] = (ob.Legal + (legal ? 1 : 0), ob.Conceded + conceded);

                        if (!d.TryGetProperty("wickets", out var wickets)) continue;
                        foreach (var wk in wickets.EnumerateArray())
                        {
                            var kind = wk.GetProperty("kind").GetString() ?? "";
                            var creditBowler = !NotBowlerWickets.Contains(kind);
                            if (!NotDismissals.Contains(kind)) innWickets++;
                            if (wk.TryGetProperty("player_out", out var po) && po.GetString() is { } outName)
                            {
                                var outId = Id(outName);
                                Arrive(outId);
                                var victim = P(outId, battingTeam);
                                if (!NotDismissals.Contains(kind))
                                {
                                    victim.Dismissed = true;
                                    victim.DismissalType = kind;
                                    if (creditBowler && type is not (BowlingType.Unknown or BowlingType.None)) victim.DismissedByBowlingType = type.ToString();
                                }
                                if (creditBowler)
                                {
                                    switch (linked(outId)?.Hand)
                                    {
                                        case BattingHand.RightHand: bw.WicketsVsRightHand++; break;
                                        case BattingHand.LeftHand: bw.WicketsVsLeftHand++; break;
                                    }
                                }
                            }
                            if (creditBowler)
                            {
                                bw.Wickets++;
                                switch (phase)
                                {
                                    case 0: bw.PowerplayWickets++; break;
                                    case 1: bw.MiddleWickets++; break;
                                    default: bw.DeathWickets++; break;
                                }
                            }
                            if (kind == "caught and bowled") bw.Catches++;
                            if (wk.TryGetProperty("fielders", out var fielders) && fielders.GetArrayLength() > 0
                                && fielders[0].TryGetProperty("name", out var fn) && fn.GetString() is { } fielderName)
                            {
                                var f = P(Id(fielderName), fieldingTeam);
                                if (kind == "caught") f.Catches++;
                                else if (kind == "stumped") f.Stumpings++;
                                else if (kind == "run out") f.RunOuts++;
                            }
                        }
                    }
                    if (byBowler.Count == 1)
                    {
                        var (id, (legalBalls, conceded)) = byBowler.First();
                        if (legalBalls >= 6 && conceded == 0) perf[id].Maidens++;
                    }
                }
                totals.Add(new InningsTotal(battingTeam, innRuns, innWickets, innBalls));
                for (var i = 0; i < order.Count; i++)
                {
                    var s = P(order[i], battingTeam);
                    s.Batted = true;
                    s.BattingPosition ??= i + 1;
                }
            }

        // Everyone in the playing list played, even without batting or bowling.
        foreach (var id in teamOf.Keys) P(id, teamOf[id]);

        var venue = info.TryGetProperty("venue", out var v) ? v.GetString() ?? "" : "";
        var city = info.TryGetProperty("city", out var c) ? c.GetString() : null;
        foreach (var (id, s) in perf)
        {
            s.Team = teamOf[id];
            s.Opponent = Other(s.Team);
            s.Venue = venue;
            s.IsHome = homeAwayForIpl && Cricsheet.FranchiseByTeamName.TryGetValue(s.Team, out var code)
                && Cricsheet.HomeCities[code].Any(h => (city ?? "").Contains(h, StringComparison.OrdinalIgnoreCase) || venue.Contains(h, StringComparison.OrdinalIgnoreCase));
        }
        return new ParsedMatch(fileId, FirstDate(info), teams.ElementAtOrDefault(0) ?? "", teams.ElementAtOrDefault(1) ?? "", venue, city, ResultText(info), perf,
            Summary(info, totals, targetOvers));
    }

    private static MatchSummary Summary(JsonElement info, List<InningsTotal> totals, int? targetOvers)
    {
        int? number = null;
        string? stage = null;
        if (info.TryGetProperty("event", out var ev))
        {
            if (ev.TryGetProperty("match_number", out var n) && n.ValueKind == JsonValueKind.Number) number = n.GetInt32();
            if (ev.TryGetProperty("stage", out var st)) stage = st.GetString();
        }
        string? winner = null;
        var noResult = false;
        if (info.TryGetProperty("outcome", out var o))
        {
            if (o.TryGetProperty("winner", out var w)) winner = w.GetString();
            else if (o.TryGetProperty("eliminator", out var e)) winner = e.GetString();
            else if (o.TryGetProperty("result", out var r) && r.GetString() is "no result") noResult = true;
        }
        int? overs = info.TryGetProperty("overs", out var ov) && ov.ValueKind == JsonValueKind.Number ? ov.GetInt32() : null;
        return new MatchSummary(number, stage, winner, noResult, overs, targetOvers, totals);
    }

    /// <summary>Copies scores and outcome onto the match row (first innings of each team; super overs excluded).</summary>
    public static void ApplySummary(Match m, ParsedMatch parsed)
    {
        var s = parsed.Summary;
        m.City = parsed.City is null ? null : parsed.City.Length <= 100 ? parsed.City : parsed.City[..100];
        m.MatchNumber = s.MatchNumber;
        m.Stage = s.Stage is { Length: > 50 } st ? st[..50] : s.Stage;
        m.Winner = s.Winner is { Length: > 100 } w ? w[..100] : s.Winner;
        m.NoResult = s.NoResult;
        m.OversLimit = s.OversLimit;
        var t1 = s.Innings.FirstOrDefault(i => i.Team == parsed.Team1);
        var t2 = s.Innings.FirstOrDefault(i => i.Team == parsed.Team2);
        m.Team1Runs = t1?.Runs; m.Team1Wickets = t1?.Wickets; m.Team1Balls = t1?.Balls;
        m.Team2Runs = t2?.Runs; m.Team2Wickets = t2?.Wickets; m.Team2Balls = t2?.Balls;
        m.Team1BattedFirst = s.Innings.Count == 0 || s.Innings[0].Team == parsed.Team1;
        m.Team2OversLimit = s.TargetOvers;
    }

    private static string? ResultText(JsonElement info)
    {
        if (!info.TryGetProperty("outcome", out var o)) return null;
        if (o.TryGetProperty("winner", out var w))
        {
            var text = $"{w.GetString()} won";
            if (o.TryGetProperty("by", out var by))
                foreach (var p in by.EnumerateObject())
                    text += $" by {p.Value.GetInt32()} {(p.Value.GetInt32() == 1 ? p.Name.TrimEnd('s') : p.Name)}";
            if (o.TryGetProperty("method", out var m)) text += $" ({m.GetString()})";
            return text;
        }
        if (o.TryGetProperty("result", out var r))
        {
            var res = r.GetString() ?? "";
            if (o.TryGetProperty("eliminator", out var e)) return $"Tie ({e.GetString()} won the super over)";
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(res);
        }
        return null;
    }
}

public sealed record CricsheetRunResult(
    DateTime StartedAt, DateTime FinishedAt, bool Succeeded, string? Error, IReadOnlyList<string> Datasets,
    int MatchesRead, int MatchesAdded, int PerformancesAdded, int PlayersLinked, int PlayersWithData, int PlayersTotal,
    IReadOnlyList<string> Unlinked);

public sealed record CricsheetStatusDto(bool Running, string? Stage, CricsheetRunResult? LastRun, DateTime? NextAutomaticRun, string Source, string Licence, string LicenceUrl);

/// <summary>Downloads the Cricsheet archives, links squad players and loads their match data.</summary>
public class CricsheetImporter(AppDbContext db, StatisticsService stats, IAuditService audit, CricsheetOptions options, IClock clock, ILogger<CricsheetImporter> log)
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("FranchiseHQ-StatsImporter/1.0");
        return c;
    }

    public async Task<CricsheetRunResult> RunAsync(Action<string> stage, CancellationToken ct)
    {
        var started = clock.UtcNow;
        var datasets = options.Datasets.Where(Cricsheet.CompetitionByDataset.ContainsKey).Distinct().ToList();
        var archives = new Dictionary<string, byte[]>();
        foreach (var ds in datasets)
        {
            stage($"Downloading {ds}");
            archives[ds] = await LoadArchiveAsync(ds, ct);
        }

        // 1) Link squad players to Cricsheet people (IPL data gives names + franchise history).
        stage("Linking players");
        var people = new Dictionary<string, CricsheetPerson>();
        var latestYear = 0;
        foreach (var (ds, zip) in archives)
            foreach (var (_, doc) in Entries(zip))
                using (doc)
                {
                    var info = doc.RootElement.GetProperty("info");
                    if (!CricsheetMatchParser.IsMaleMatch(info)) continue;
                    var year = CricsheetMatchParser.FirstDate(info).Year;
                    var isIpl = ds == "ipl";
                    if (isIpl) latestYear = Math.Max(latestYear, year);
                    var reg = CricsheetMatchParser.Registry(info);
                    foreach (var team in info.GetProperty("players").EnumerateObject())
                        foreach (var n in team.Value.EnumerateArray())
                        {
                            var name = n.GetString()!;
                            if (!reg.TryGetValue(name, out var id)) continue;
                            if (!people.TryGetValue(id, out var person)) people[id] = person = new CricsheetPerson(id);
                            person.Names.Add(name);
                            if (!isIpl)
                            {
                                var key = "T20I:" + team.Name;
                                person.FranchiseYears[key] = Math.Max(person.FranchiseYears.GetValueOrDefault(key), year);
                                continue;
                            }
                            person.LastYear = Math.Max(person.LastYear, year);
                            if (Cricsheet.FranchiseByTeamName.TryGetValue(team.Name, out var code))
                                person.FranchiseYears[code] = Math.Max(person.FranchiseYears.GetValueOrDefault(code), year);
                        }
                }

        var real = await db.Players.Where(p => !p.IsDemo)
            .Select(p => new { Player = p, Code = p.CurrentFranchise != null ? p.CurrentFranchise.Code : null, Country = p.Country!.Name, p.IsOverseas }).ToListAsync(ct);
        var taken = real.Where(r => r.Player.CricsheetId != null).Select(r => r.Player.CricsheetId!).ToHashSet();
        var linkedNow = 0;
        foreach (var r in real.Where(r => r.Player.CricsheetId is null))
            if (RealSquads.CricsheetIds.TryGetValue(r.Player.FullName, out var vetted) && taken.Add(vetted))
            {
                r.Player.CricsheetId = vetted;
                linkedNow++;
            }
        if (latestYear == 0) latestYear = clock.Today.Year;
        if (people.Count > 0)
        {
            var open = real.Where(r => r.Player.CricsheetId is null).Select(r => new SquadPlayerRef(r.Player.Id, r.Player.FullName, r.Code, r.IsOverseas ? r.Country : null)).ToList();
            var free = people.Values.Where(p => !taken.Contains(p.Id)).ToList();
            foreach (var (playerId, csId) in CricsheetNameMatcher.Match(open, free, latestYear))
            {
                real.First(r => r.Player.Id == playerId).Player.CricsheetId = csId;
                linkedNow++;
            }
        }
        await db.SaveChangesAsync(ct);

        var linked = await db.Players.Where(p => p.CricsheetId != null)
            .Select(p => new { p.CricsheetId, p.Id, p.BattingHand, p.BowlingType }).ToListAsync(ct);
        var byCsId = linked.ToDictionary(x => x.CricsheetId!, x => new LinkedPlayer(x.Id, x.BattingHand, x.BowlingType));
        LinkedPlayer? Lookup(string id) => byCsId.GetValueOrDefault(id);

        // 2) Load matches that involve linked players (new matches, and new players in already-loaded matches).
        var existing = await db.Matches.Where(m => m.ExternalRef != null && m.ExternalRef.StartsWith(Cricsheet.RefPrefix))
            .Select(m => new { m.ExternalRef, m.Id }).ToDictionaryAsync(m => m.ExternalRef!, m => m.Id, ct);
        // Rows loaded before scores were imported: fill them in on this run.
        var needScores = await db.Matches.Where(m => m.ExternalRef != null && m.ExternalRef.StartsWith(Cricsheet.RefPrefix)
                && m.Team1Runs == null && !m.NoResult && m.Winner == null)
            .ToDictionaryAsync(m => m.ExternalRef!, ct);
        var scoresFilled = 0;
        var existingPairs = (await db.PlayerMatchStatistics.Where(s => s.Match!.ExternalRef != null && s.Match.ExternalRef.StartsWith(Cricsheet.RefPrefix))
            .Select(s => new { s.MatchId, s.PlayerId }).ToListAsync(ct)).Select(x => (x.MatchId, x.PlayerId)).ToHashSet();
        var competitions = await db.Competitions.ToDictionaryAsync(c => c.Code, c => c.Id, ct);

        var newMatches = new List<Match>();
        var newLines = new List<PlayerMatchStatistic>();
        var touched = new HashSet<Guid>();
        var read = 0;
        foreach (var ds in datasets)
        {
            stage($"Reading {ds} matches");
            if (!competitions.TryGetValue(Cricsheet.CompetitionByDataset[ds], out var competitionId))
            {
                log.LogWarning("Competition {Code} is missing; skipping {Dataset}", Cricsheet.CompetitionByDataset[ds], ds);
                continue;
            }
            foreach (var (fileId, doc) in Entries(archives[ds]))
                using (doc)
                {
                    var root = doc.RootElement;
                    var info = root.GetProperty("info");
                    if (!CricsheetMatchParser.IsMaleMatch(info)) continue;
                    read++;
                    var reg = CricsheetMatchParser.Registry(info);
                    var keepAll = ds == "ipl"; // every IPL match: fixtures and points tables need them all
                    if (!keepAll && !reg.Values.Any(byCsId.ContainsKey)) continue;
                    var reference = Cricsheet.RefPrefix + fileId;
                    var known = existing.TryGetValue(reference, out var matchId);
                    var backfill = needScores.TryGetValue(reference, out var oldRow);
                    if (known && !backfill && reg.Values.Where(byCsId.ContainsKey).All(id => existingPairs.Contains((matchId, byCsId[id].PlayerId)))) continue;

                    var parsed = CricsheetMatchParser.Parse(fileId, root, Lookup, ds == "ipl");
                    if (backfill)
                    {
                        CricsheetMatchParser.ApplySummary(oldRow!, parsed);
                        scoresFilled++;
                    }
                    if (!known)
                    {
                        var match = new Match
                        {
                            CompetitionId = competitionId, Year = parsed.Date.Year, Date = parsed.Date, Venue = Cut(parsed.Venue, 200),
                            Team1 = Cut(parsed.Team1, 100), Team2 = Cut(parsed.Team2, 100), Result = parsed.Result is null ? null : Cut(parsed.Result, 200),
                            DataSource = Cricsheet.Source, ExternalRef = reference,
                        };
                        CricsheetMatchParser.ApplySummary(match, parsed);
                        newMatches.Add(match);
                        matchId = match.Id;
                        existing[reference] = matchId;
                    }
                    foreach (var (csId, line) in parsed.Performances)
                    {
                        if (!byCsId.TryGetValue(csId, out var lp) || !existingPairs.Add((matchId, lp.PlayerId))) continue;
                        line.MatchId = matchId;
                        line.PlayerId = lp.PlayerId;
                        line.Team = Cut(line.Team, 100);
                        line.Opponent = Cut(line.Opponent, 100);
                        line.Venue = Cut(line.Venue, 200);
                        newLines.Add(line);
                        touched.Add(lp.PlayerId);
                    }
                }
        }

        stage("Saving");
        if (scoresFilled > 0) await db.SaveChangesAsync(ct);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await DemoSeeder.BulkInsertAsync(db, newMatches, "Matches", ct);
            await DemoSeeder.BulkInsertAsync(db, newLines, "PlayerMatchStatistics", ct);
            await tx.CommitAsync(ct);
        });

        if (archives.TryGetValue("ipl", out var iplArchive) && competitions.TryGetValue("IPL", out var iplCompetition))
        {
            stage("Building scorecards, leaderboards and match-ups");
            var built = await CricsheetArchive.RebuildAsync(db, Entries(iplArchive), iplCompetition, ct);
            log.LogInformation("IPL archive: {Cards} scorecards added, {People} person-seasons, {Matchups} batter-bowler match-ups", built.Scorecards, built.People, built.Matchups);
        }

        stage("Recalculating statistics");
        if (touched.Count > 0) await stats.RecalculateAsync(touched, ct);

        var withData = await db.PlayerMatchStatistics.Where(s => s.Match!.ExternalRef != null && s.Match.ExternalRef.StartsWith(Cricsheet.RefPrefix))
            .Select(s => s.PlayerId).Distinct().CountAsync(ct);
        var unlinked = await db.Players.Where(p => !p.IsDemo && p.CricsheetId == null)
            .OrderBy(p => p.CurrentFranchise!.Code).ThenBy(p => p.FullName)
            .Select(p => p.FullName + (p.CurrentFranchise != null ? " (" + p.CurrentFranchise.Code + ")" : "")).ToListAsync(ct);
        var result = new CricsheetRunResult(started, clock.UtcNow, true, null, datasets, read, newMatches.Count, newLines.Count, linkedNow,
            withData, real.Count, unlinked);
        audit.Record("stats.cricsheet-imported", "Statistics", null, new { result.MatchesAdded, result.PerformancesAdded, result.PlayersLinked });
        await SaveLastRunAsync(db, result, clock, ct);
        log.LogInformation("Cricsheet import: {Matches} matches read, {Added} added, {Lines} performances, {Linked} players linked",
            read, newMatches.Count, newLines.Count, linkedNow);
        return result;
    }

    public static async Task SaveLastRunAsync(AppDbContext db, CricsheetRunResult result, IClock clock, CancellationToken ct)
    {
        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == Cricsheet.LastRunKey, ct);
        if (row is null)
            db.SystemSettings.Add(row = new SystemSetting { Key = Cricsheet.LastRunKey, Description = "Result of the last Cricsheet statistics import." });
        row.Value = JsonSerializer.Serialize(result);
        row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

    private async Task<byte[]> LoadArchiveAsync(string dataset, CancellationToken ct)
    {
        var file = $"{dataset}_json.zip";
        if (options.LocalFolder is { Length: > 0 } folder && File.Exists(Path.Combine(folder, file)))
            return await File.ReadAllBytesAsync(Path.Combine(folder, file), ct);
        // Keep a local copy and only download again when Cricsheet has published a newer archive.
        var cacheDir = Path.Combine(Path.GetTempPath(), "franchisehq-cricsheet");
        Directory.CreateDirectory(cacheDir);
        var cached = Path.Combine(cacheDir, file);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(options.BaseUrl), file));
        if (File.Exists(cached)) request.Headers.IfModifiedSince = File.GetLastWriteTimeUtc(cached);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified && File.Exists(cached))
            return await File.ReadAllBytesAsync(cached, ct);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        await File.WriteAllBytesAsync(cached, bytes, ct);
        if (response.Content.Headers.LastModified is { } modified) File.SetLastWriteTimeUtc(cached, modified.UtcDateTime);
        return bytes;
    }

    private static IEnumerable<(string FileId, JsonDocument Doc)> Entries(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = entry.Open();
            yield return (Path.GetFileNameWithoutExtension(entry.Name), JsonDocument.Parse(s));
        }
    }
}

/// <summary>Runs one import at a time in the background and keeps its progress for the status endpoint.</summary>
public class CricsheetImportRunner(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, CricsheetOptions options, PhotoImportRunner photos, ILogger<CricsheetImportRunner> log)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile string? stage;

    public bool IsRunning => stage is not null;

    /// <summary>Starts an import unless one is already running. Returns false when busy.</summary>
    public bool TryStart()
    {
        if (!gate.Wait(0)) return false;
        stage = "Starting";
        _ = Task.Run(async () =>
        {
            var started = DateTime.UtcNow;
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CricsheetImporter>().RunAsync(s => stage = s, lifetime.ApplicationStopping);
                // Newly linked players can now be matched to free photos: refresh them after every successful import.
                if (options.RefreshPhotosAfterImport && photos.TryStart()) log.LogInformation("Photo refresh started after the Cricsheet import");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Cricsheet import failed");
                try
                {
                    using var scope = scopes.CreateScope();
                    var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                    await CricsheetImporter.SaveLastRunAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                        new CricsheetRunResult(started, clock.UtcNow, false, ex.Message, options.Datasets, 0, 0, 0, 0, 0, 0, []), clock, CancellationToken.None);
                }
                catch (Exception inner) { log.LogError(inner, "Could not record the failed Cricsheet import"); }
            }
            finally
            {
                stage = null;
                gate.Release();
            }
        });
        return true;
    }

    public async Task<CricsheetStatusDto> StatusAsync(AppDbContext db, CancellationToken ct)
    {
        var current = stage;
        var last = await LastRunAsync(db, ct);
        DateTime? next = options.AutoImportDays > 0 ? (last?.StartedAt ?? DateTime.UtcNow).AddDays(options.AutoImportDays) : null;
        return new CricsheetStatusDto(current is not null, current, last, next, Cricsheet.Source,
            "Open Data Commons Attribution License (ODC-BY 1.0)", "https://opendatacommons.org/licenses/by/1-0/");
    }

    public static async Task<CricsheetRunResult?> LastRunAsync(AppDbContext db, CancellationToken ct)
    {
        var json = await db.SystemSettings.AsNoTracking().Where(s => s.Key == Cricsheet.LastRunKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<CricsheetRunResult>(json);
    }
}

/// <summary>Starts the Cricsheet import every <see cref="CricsheetOptions.AutoImportDays"/> days (checked hourly).</summary>
public class CricsheetScheduler(IServiceScopeFactory scopes, CricsheetImportRunner runner, CricsheetOptions options, ILogger<CricsheetScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.AutoImportDays <= 0) return;
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var last = await CricsheetImportRunner.LastRunAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), stoppingToken);
                if ((last is null || last.StartedAt.AddDays(options.AutoImportDays) <= DateTime.UtcNow) && runner.TryStart())
                    log.LogInformation("Automatic Cricsheet import started");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Cricsheet schedule check failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
