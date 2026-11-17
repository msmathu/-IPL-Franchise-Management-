using Ipl.Application.Rules;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Squads;

/// <summary>One squad member with what composition rules need.</summary>
public record SquadMember(
    Guid PlayerId, string Name, string RoleCode, string RoleName, bool IsWicketKeeper, bool IsOverseas, bool IsCapped,
    bool CanBowl, decimal Price, decimal Salary, int Age, string Availability, string Acquisition);

public record CompositionDto(
    int Total, int Batters, int Bowlers, int AllRounders, int WicketKeepers, int Overseas, int Uncapped, int Domestic,
    int MaxSquad, int MinSquad, int MaxOverseas, int SlotsRemaining, int OverseasSlotsRemaining, IReadOnlyList<string> Warnings);

public static class SquadMath
{
    public static bool IsBowlingOption(string roleCode, BowlingType type) => roleCode is "bowler" or "allrounder" || type != BowlingType.None && roleCode != "wk_batter";

    public static CompositionDto Compose(IReadOnlyCollection<SquadMember> squad, SeasonRules rules)
    {
        var max = rules.Int(RuleKeys.MaxSquadSize);
        var min = rules.Int(RuleKeys.MinSquadSize);
        var maxOs = rules.Int(RuleKeys.MaxOverseasInSquad);
        var overseas = squad.Count(m => m.IsOverseas);
        List<string> warnings = [];
        if (squad.Count > max) warnings.Add($"Squad has {squad.Count} players; the maximum is {max}.");
        if (squad.Count < min) warnings.Add($"Squad has {squad.Count} players; at least {min} are required.");
        if (overseas > maxOs) warnings.Add($"{overseas} overseas players; the maximum is {maxOs}.");
        if (!squad.Any(m => m.IsWicketKeeper)) warnings.Add("No wicketkeeper in the squad.");
        if (squad.Count(m => m.CanBowl) < rules.IntOr(RuleKeys.MinBowlingOptions, 5)) warnings.Add("Fewer bowling options than a Playing XI needs.");
        return new CompositionDto(
            squad.Count,
            squad.Count(m => m.RoleCode is "batter"),
            squad.Count(m => m.RoleCode is "bowler"),
            squad.Count(m => m.RoleCode is "allrounder"),
            squad.Count(m => m.IsWicketKeeper),
            overseas,
            squad.Count(m => !m.IsCapped),
            squad.Count(m => !m.IsOverseas),
            max, min, maxOs, Math.Max(0, max - squad.Count), Math.Max(0, maxOs - overseas), warnings);
    }

    /// <summary>Active squad of a franchise for a season, with each player's active contract salary.</summary>
    public static async Task<List<SquadMember>> LoadAsync(IAppDbContext db, Guid franchiseId, int seasonId, DateOnly today, CancellationToken ct)
    {
        var rows = await db.FranchiseSquads.AsNoTracking()
            .Where(s => s.FranchiseId == franchiseId && s.SeasonId == seasonId && s.Status == SquadMemberStatus.Active)
            .Select(s => new
            {
                s.PlayerId, s.Player!.FullName, Role = s.Player.PrimaryRole!.Code, RoleName = s.Player.PrimaryRole.Name, s.Player.IsWicketKeeper,
                s.Player.IsOverseas, s.Player.IsCapped, s.Player.BowlingType, s.Price, s.Player.DateOfBirth, s.Acquisition,
                // Contracts are confidential: IgnoreQueryFilters is safe here because only the salary number is used for rule checks.
                Salary = db.PlayerContracts.IgnoreQueryFilters().Where(c => c.PlayerId == s.PlayerId && c.FranchiseId == franchiseId && c.Status == ContractStatus.Active)
                    .Select(c => (decimal?)c.AnnualSalary).FirstOrDefault(),
                Avail = db.PlayerAvailability.Where(a => a.PlayerId == s.PlayerId).Select(a => (AvailabilityStatus?)a.Status).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows.Select(r => new SquadMember(r.PlayerId, r.FullName, r.Role, r.RoleName, r.IsWicketKeeper, r.IsOverseas, r.IsCapped,
            IsBowlingOption(r.Role, r.BowlingType), r.Price, r.Salary ?? r.Price, Players.PlayerService.AgeOn(r.DateOfBirth, today),
            (r.Avail ?? AvailabilityStatus.Available).ToString(), r.Acquisition.ToString())).ToList();
    }
}
