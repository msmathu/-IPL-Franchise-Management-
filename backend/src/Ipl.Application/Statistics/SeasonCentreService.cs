using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Statistics;

public record TeamBadgeDto(string Name, string Code, string? FranchiseId, string? PrimaryColor, string? SecondaryColor);

public record TeamScoreDto(TeamBadgeDto Team, int? Runs, int? Wickets, int? Balls, string? Overs, bool BattedFirst);

public record FixtureDto(
    Guid Id, DateOnly Date, int? MatchNumber, string? Stage, string Venue, string? City,
    TeamScoreDto Team1, TeamScoreDto Team2, string? Result, string? Winner, bool NoResult, string DataSource);

public record StandingDto(
    int Position, TeamBadgeDto Team, int Played, int Won, int Lost, int NoResult, int Points, decimal? NetRunRate,
    IReadOnlyList<string> Form);

public record SeasonCentreYearsDto(IReadOnlyList<int> Years, string Competition, string Source);

/// <summary>
/// Results and points tables calculated from imported match records (no hand-entered or invented numbers).
/// Points: 2 for a win (including super-over wins), 1 each for no result. Net run rate: runs per over scored minus
/// runs per over conceded; a side bowled out counts its full quota of overs. Play-off matches are not in the table.
/// </summary>
public class SeasonCentreService(IAppDbContext db, ICurrentUser me, IAppCache cache)
{
    private const string Competition = "IPL";

    public async Task<SeasonCentreYearsDto> YearsAsync(CancellationToken ct)
    {
        me.Require(Perm.StatsRead);
        var years = await db.Matches.AsNoTracking().Where(m => m.Competition!.Code == Competition && m.Team1Runs != null || m.Competition!.Code == Competition && m.NoResult)
            .Select(m => m.Year).Distinct().OrderByDescending(y => y).ToListAsync(ct);
        var source = await db.Matches.AsNoTracking().Where(m => m.Competition!.Code == Competition && m.ExternalRef != null)
            .Select(m => m.DataSource).FirstOrDefaultAsync(ct) ?? "";
        return new SeasonCentreYearsDto(years, "Indian Premier League", source);
    }

    public async Task<IReadOnlyList<FixtureDto>> FixturesAsync(int year, string? team, CancellationToken ct)
    {
        me.Require(Perm.StatsRead);
        var badges = await BadgesAsync(ct);
        var rows = await db.Matches.AsNoTracking()
            .Where(m => m.Competition!.Code == Competition && m.Year == year && m.ExternalRef != null)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.MatchNumber).ToListAsync(ct);
        var list = rows.Select(m => new FixtureDto(
                m.Id, m.Date, m.MatchNumber, m.Stage, m.Venue, m.City,
                Score(badges(m.Team1), m.Team1Runs, m.Team1Wickets, m.Team1Balls, m.Team1BattedFirst),
                Score(badges(m.Team2), m.Team2Runs, m.Team2Wickets, m.Team2Balls, !m.Team1BattedFirst),
                m.Result, m.Winner, m.NoResult, m.DataSource))
            .ToList();
        if (!string.IsNullOrWhiteSpace(team))
            list = list.Where(f => f.Team1.Team.Code.Equals(team, StringComparison.OrdinalIgnoreCase) || f.Team2.Team.Code.Equals(team, StringComparison.OrdinalIgnoreCase)).ToList();
        return list;
    }

    public async Task<IReadOnlyList<StandingDto>> StandingsAsync(int year, CancellationToken ct)
    {
        me.Require(Perm.StatsRead);
        return await cache.GetOrCreateAsync($"stats:standings:{year}", async () =>
        {
            var badges = await BadgesAsync(ct);
            var matches = await db.Matches.AsNoTracking()
                .Where(m => m.Competition!.Code == Competition && m.Year == year && m.ExternalRef != null && m.Stage == null)
                .OrderBy(m => m.Date).ThenBy(m => m.MatchNumber).ToListAsync(ct);
            var table = new Dictionary<string, Row>();
            Row R(string name) => table.TryGetValue(name, out var r) ? r : table[name] = new Row(name);
            foreach (var m in matches)
            {
                var a = R(m.Team1);
                var b = R(m.Team2);
                a.Played++; b.Played++;
                if (m.NoResult || m.Winner is null)
                {
                    a.NoResult++; b.NoResult++;
                    a.Points++; b.Points++;
                    a.Form.Add("N"); b.Form.Add("N");
                    continue;
                }
                var (win, lose) = m.Winner == m.Team1 ? (a, b) : (b, a);
                win.Won++; win.Points += 2; win.Form.Add("W");
                lose.Lost++; lose.Form.Add("L");
                // Net run rate (completed results only).
                var limit1 = (m.OversLimit ?? 20) * 6;
                var limit2 = (m.Team2OversLimit ?? m.OversLimit ?? 20) * 6;
                var firstLimit = m.Team1BattedFirst ? limit1 : limit2;
                var secondLimit = m.Team1BattedFirst ? limit2 : limit1;
                var balls1 = m.Team1Wickets >= 10 ? (m.Team1BattedFirst ? firstLimit : secondLimit) : m.Team1Balls ?? 0;
                var balls2 = m.Team2Wickets >= 10 ? (m.Team1BattedFirst ? secondLimit : firstLimit) : m.Team2Balls ?? 0;
                if (m.Team1Runs is { } r1 && m.Team2Runs is { } r2 && balls1 > 0 && balls2 > 0)
                {
                    a.RunsFor += r1; a.BallsFaced += balls1; a.RunsAgainst += r2; a.BallsBowled += balls2;
                    b.RunsFor += r2; b.BallsFaced += balls2; b.RunsAgainst += r1; b.BallsBowled += balls1;
                }
            }
            var ordered = table.Values
                .Select(r => (Row: r, Nrr: r.BallsFaced > 0 && r.BallsBowled > 0
                    ? Math.Round((decimal)r.RunsFor * 6 / r.BallsFaced - (decimal)r.RunsAgainst * 6 / r.BallsBowled, 3) : (decimal?)null))
                .OrderByDescending(x => x.Row.Points).ThenByDescending(x => x.Nrr ?? decimal.MinValue).ThenBy(x => x.Row.Name)
                .ToList();
            return (IReadOnlyList<StandingDto>)ordered.Select((x, i) => new StandingDto(i + 1, badges(x.Row.Name), x.Row.Played, x.Row.Won, x.Row.Lost,
                x.Row.NoResult, x.Row.Points, x.Nrr, x.Row.Form.TakeLast(5).ToList())).ToList();
        }, TimeSpan.FromMinutes(30), ct);
    }

    private static TeamScoreDto Score(TeamBadgeDto team, int? runs, int? wickets, int? balls, bool battedFirst) =>
        new(team, runs, wickets, balls, balls is { } b ? $"{b / 6}{(b % 6 > 0 ? "." + b % 6 : "")}" : null, battedFirst);

    /// <summary>Team badge for a match team name: current franchise colours where the team maps to one.</summary>
    private async Task<Func<string, TeamBadgeDto>> BadgesAsync(CancellationToken ct)
    {
        var franchises = await db.Franchises.AsNoTracking()
            .Select(f => new { f.Id, f.Code, f.PrimaryColor, f.SecondaryColor }).ToListAsync(ct);
        var byCode = franchises.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);
        return name =>
        {
            var code = TeamNames.ShortCode(name);
            return TeamNames.FranchiseCodeByName.ContainsKey(name) && byCode.TryGetValue(code, out var f)
                ? new TeamBadgeDto(name, code, f.Id.ToString(), f.PrimaryColor, f.SecondaryColor)
                : new TeamBadgeDto(name, code, null, null, null);
        };
    }

    private sealed class Row(string name)
    {
        public string Name { get; } = name;
        public int Played, Won, Lost, NoResult, Points, RunsFor, BallsFaced, RunsAgainst, BallsBowled;
        public List<string> Form { get; } = [];
    }
}
