using Ipl.Application.Analytics;
using Ipl.Application.Rules;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Squads;

public record SquadPlayerDto(
    Guid PlayerId, string Name, string Role, string RoleCode, bool IsWicketKeeper, bool IsOverseas, bool IsCapped, bool CanBowl,
    int? Age, decimal? Price, string Acquisition, string Availability, decimal? Rating, string FormTrend, string? PhotoUrl,
    bool IsCaptain = false, int Matches = 0, int Runs = 0, int Wickets = 0);

public record SquadDto(
    Guid FranchiseId, string FranchiseName, int SeasonId, string Season, CompositionDto Composition, TeamStrengthDto Strength,
    decimal PurseRemaining, decimal SquadValue, IReadOnlyList<SquadPlayerDto> Players, IReadOnlyList<SquadHistoryDto> Departed,
    string Code = "", string PrimaryColor = "", string SecondaryColor = "");

public record SquadHistoryDto(Guid PlayerId, string Name, string Status, DateTime? LeftAt);

public record XIPlayerRequest(Guid PlayerId, int? BattingOrder, bool IsImpactSubstitute, bool IsBowlingOption);

public record SavePlayingXIRequest(string Name, string? Opponent, string? Venue, Guid? CaptainId, Guid? ViceCaptainId, Guid? WicketKeeperId,
    bool IsPreferred, string? Notes, IReadOnlyList<XIPlayerRequest> Players);

public record XIPlayerDto(Guid PlayerId, string Name, string Role, bool IsOverseas, bool IsWicketKeeper, int? BattingOrder, bool IsImpactSubstitute, bool IsBowlingOption, string Availability);

public record PlayingXIDto(
    Guid Id, string Name, string? Opponent, string? Venue, Guid? CaptainId, Guid? ViceCaptainId, Guid? WicketKeeperId, bool IsPreferred,
    string? Notes, IReadOnlyList<XIPlayerDto> Players, IReadOnlyList<string> Warnings, DateTime UpdatedAt);

public record XIValidationDto(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, int Overseas, int BowlingOptions, decimal Strength);

public class SquadService(IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, RatingService ratings, IAuditService audit, Players.PlayerService players)
{
    public async Task<SquadDto> GetAsync(Guid franchiseId, CancellationToken ct)
    {
        me.Require(Perm.SquadRead);
        var season = await rules.CurrentSeasonAsync(ct);
        var seasonRules = await rules.ForSeasonAsync(season.Id, ct);
        var franchise = await db.Franchises.AsNoTracking().FirstOrDefaultAsync(f => f.Id == franchiseId, ct) ?? throw new NotFoundException("Franchise");
        var squad = await SquadMath.LoadAsync(db, franchiseId, season.Id, clock.Today, ct);
        var rated = await ratings.RateManyAsync(squad.Select(m => m.PlayerId).ToList(), ct);
        var strength = RatingService.TeamStrength(squad.Select(m => (m.PlayerId, rated.TryGetValue(m.PlayerId, out var r) ? r.Rating : 40m, m.IsOverseas, m.IsWicketKeeper, m.CanBowl)).ToList(),
            seasonRules.Int(RuleKeys.MaxOverseasInXI), seasonRules.IntOr(RuleKeys.MinBowlingOptions, 5));
        var purse = await db.FranchiseSeasons.AsNoTracking().Where(s => s.FranchiseId == franchiseId && s.SeasonId == season.Id).Select(s => s.PurseRemaining).FirstOrDefaultAsync(ct);
        var departed = await db.FranchiseSquads.AsNoTracking()
            .Where(s => s.FranchiseId == franchiseId && s.SeasonId == season.Id && s.Status != SquadMemberStatus.Active)
            .OrderByDescending(s => s.LeftAt)
            .Select(s => new SquadHistoryDto(s.PlayerId, s.Player!.FullName, s.Status.ToString(), s.LeftAt)).ToListAsync(ct);
        var confidential = me.CanAccessFranchise(franchiseId);
        var captains = (await db.FranchiseStaff.AsNoTracking().Where(s => s.FranchiseId == franchiseId && s.Position.StartsWith("Captain"))
            .Select(s => s.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var t20 = await players.PrimaryT20CompetitionIdAsync(ct);
        var ids = squad.Select(m => m.PlayerId).ToList();
        var careers = await db.PlayerCareerStatistics.AsNoTracking().Where(c => c.CompetitionId == t20 && ids.Contains(c.PlayerId))
            .Select(c => new { c.PlayerId, c.Matches, c.Runs, c.Wickets }).ToDictionaryAsync(c => c.PlayerId, ct);
        return new SquadDto(franchiseId, franchise.Name, season.Id, season.Name, SquadMath.Compose(squad, seasonRules), strength,
            confidential ? purse : 0, squad.Sum(m => m.Price),
            squad.OrderBy(m => m.RoleCode).ThenByDescending(m => rated.GetValueOrDefault(m.PlayerId)?.Rating)
                .Select(m =>
                {
                    var r = rated.GetValueOrDefault(m.PlayerId);
                    var form = r?.Factors.FirstOrDefault(f => f.Factor == "Recent form");
                    var career = r?.Factors.FirstOrDefault(f => f.Factor == "Career performance");
                    var trend = r is null || r.Confidence == "None" ? "No statistics"
                        : form is null || career is null ? "Unknown"
                        : form.Score > career.Score + 8 ? "Rising"
                        : form.Score < career.Score - 8 ? "Falling" : "Steady";
                    return new SquadPlayerDto(m.PlayerId, m.Name, m.RoleName, m.RoleCode, m.IsWicketKeeper, m.IsOverseas, m.IsCapped, m.CanBowl, m.Age,
                        m.Price > 0 ? m.Price : null, m.Acquisition, m.Availability, r is null || r.Confidence == "None" ? null : r.Rating, trend, m.PhotoUrl,
                        captains.Contains(m.Name), careers.GetValueOrDefault(m.PlayerId)?.Matches ?? 0, careers.GetValueOrDefault(m.PlayerId)?.Runs ?? 0,
                        careers.GetValueOrDefault(m.PlayerId)?.Wickets ?? 0);
                }).ToList(),
            departed, franchise.Code, franchise.PrimaryColor, franchise.SecondaryColor);
    }

    // ---------------- Playing XI ----------------

    public async Task<IReadOnlyList<PlayingXIDto>> ListXIsAsync(CancellationToken ct)
    {
        me.Require(Perm.SquadRead);
        var fid = me.RequireFranchise();
        var season = await rules.CurrentSeasonAsync(ct);
        var xis = await db.PlayingXIs.AsNoTracking().Include(x => x.Players).Where(x => x.FranchiseId == fid && x.SeasonId == season.Id)
            .OrderByDescending(x => x.IsPreferred).ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt).ToListAsync(ct);
        List<PlayingXIDto> result = [];
        foreach (var xi in xis) result.Add(await ToDtoAsync(xi, fid, season.Id, ct));
        return result;
    }

    public async Task<XIValidationDto> ValidateAsync(SavePlayingXIRequest req, CancellationToken ct)
    {
        me.Require(Perm.SquadRead);
        var fid = me.RequireFranchise();
        var season = await rules.CurrentSeasonAsync(ct);
        return await ValidateInternalAsync(req, fid, season.Id, ct);
    }

    public async Task<Guid> SaveXIAsync(Guid? id, SavePlayingXIRequest req, CancellationToken ct)
    {
        me.Require(Perm.PlayingXIManage);
        var fid = me.RequireFranchise();
        var season = await rules.CurrentSeasonAsync(ct);
        if (string.IsNullOrWhiteSpace(req.Name)) throw new AppValidationException("name", "Name this combination.");
        var validation = await ValidateInternalAsync(req, fid, season.Id, ct);
        if (validation.Errors.Count > 0) throw new AppValidationException("players", string.Join(" ", validation.Errors));

        PlayingXI xi;
        if (id is { } existingId)
        {
            xi = await db.PlayingXIs.Include(x => x.Players).FirstOrDefaultAsync(x => x.Id == existingId, ct) ?? throw new NotFoundException("Playing XI");
            db.PlayingXIPlayers.RemoveRange(xi.Players);
            xi.Players.Clear();
        }
        else
        {
            xi = new PlayingXI { FranchiseId = fid, SeasonId = season.Id };
            db.PlayingXIs.Add(xi);
        }
        if (req.IsPreferred)
            foreach (var other in await db.PlayingXIs.Where(x => x.FranchiseId == fid && x.SeasonId == season.Id && x.IsPreferred && x.Id != xi.Id).ToListAsync(ct))
                other.IsPreferred = false;
        xi.Name = req.Name.Trim();
        xi.Opponent = req.Opponent?.Trim();
        xi.Venue = req.Venue?.Trim();
        xi.CaptainId = req.CaptainId;
        xi.ViceCaptainId = req.ViceCaptainId;
        xi.WicketKeeperId = req.WicketKeeperId;
        xi.IsPreferred = req.IsPreferred;
        xi.Notes = req.Notes?.Trim();
        foreach (var p in req.Players)
            xi.Players.Add(new PlayingXIPlayer { PlayerId = p.PlayerId, BattingOrder = p.IsImpactSubstitute ? null : p.BattingOrder, IsImpactSubstitute = p.IsImpactSubstitute, IsBowlingOption = p.IsBowlingOption });
        audit.Record(id is null ? "playingxi.created" : "playingxi.updated", "PlayingXI", xi.Id, new { xi.Name }, fid);
        await db.SaveChangesAsync(ct);
        return xi.Id;
    }

    public async Task DeleteXIAsync(Guid id, CancellationToken ct)
    {
        me.Require(Perm.PlayingXIManage);
        var xi = await db.PlayingXIs.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Playing XI");
        db.PlayingXIs.Remove(xi);
        audit.Record("playingxi.deleted", "PlayingXI", id, new { xi.Name }, xi.FranchiseId);
        await db.SaveChangesAsync(ct);
    }

    private async Task<XIValidationDto> ValidateInternalAsync(SavePlayingXIRequest req, Guid fid, int seasonId, CancellationToken ct)
    {
        var r = await rules.ForSeasonAsync(seasonId, ct);
        var squad = (await SquadMath.LoadAsync(db, fid, seasonId, clock.Today, ct)).ToDictionary(m => m.PlayerId);
        List<string> errors = [];
        List<string> warnings = [];
        var starters = req.Players.Where(p => !p.IsImpactSubstitute).ToList();
        var subs = req.Players.Where(p => p.IsImpactSubstitute).ToList();
        if (req.Players.Select(p => p.PlayerId).Distinct().Count() != req.Players.Count) errors.Add("A player is listed twice.");
        if (req.Players.Any(p => !squad.ContainsKey(p.PlayerId))) errors.Add("Every player must be in the current squad.");
        if (starters.Count != 11) errors.Add($"The starting XI must have 11 players ({starters.Count} selected).");
        var orders = starters.Where(p => p.BattingOrder is not null).Select(p => p.BattingOrder!.Value).ToList();
        if (orders.Count != starters.Count || orders.Distinct().Count() != orders.Count || orders.Any(o => o is < 1 or > 11))
            errors.Add("Give every starter a unique batting position from 1 to 11.");

        var impactEnabled = r.BoolOr(RuleKeys.ImpactPlayerEnabled, false);
        var maxSubs = r.IntOr(RuleKeys.ImpactSubstitutes, 0);
        if (subs.Count > 0 && !impactEnabled) errors.Add("Impact Player substitutes are not allowed this season.");
        if (impactEnabled && subs.Count > maxSubs) errors.Add($"Name at most {maxSubs} substitutes.");
        if (impactEnabled && subs.Count < maxSubs) warnings.Add($"{maxSubs - subs.Count} substitute slot(s) unused.");

        var starterIds = starters.Select(p => p.PlayerId).ToHashSet();
        var maxOs = r.Int(RuleKeys.MaxOverseasInXI);
        var overseas = starters.Count(p => squad.TryGetValue(p.PlayerId, out var m) && m.IsOverseas);
        if (overseas > maxOs) errors.Add($"{overseas} overseas players in the XI; the maximum is {maxOs}.");
        if (impactEnabled && overseas >= maxOs && subs.Any(p => squad.TryGetValue(p.PlayerId, out var m) && m.IsOverseas))
            warnings.Add("An overseas substitute can only come in if the XI has fewer overseas players than the limit.");

        var bowling = starters.Count(p => p.IsBowlingOption);
        var minBowl = r.IntOr(RuleKeys.MinBowlingOptions, 5);
        if (bowling < minBowl) warnings.Add($"Only {bowling} bowling options (at least {minBowl} recommended by the season rules).");
        foreach (var p in starters.Where(p => p.IsBowlingOption && squad.TryGetValue(p.PlayerId, out var m) && !m.CanBowl))
            warnings.Add($"{squad[p.PlayerId].Name} is marked as a bowling option but is not a recognised bowler.");

        if (req.WicketKeeperId is null || !starterIds.Contains(req.WicketKeeperId.Value)) errors.Add("Choose a wicketkeeper from the starting XI.");
        else if (squad.TryGetValue(req.WicketKeeperId.Value, out var wk) && !wk.IsWicketKeeper) warnings.Add($"{wk.Name} is not a specialist wicketkeeper.");
        if (req.CaptainId is null || !starterIds.Contains(req.CaptainId.Value)) errors.Add("Choose a captain from the starting XI.");
        if (req.ViceCaptainId is not null && !starterIds.Contains(req.ViceCaptainId.Value)) errors.Add("The vice-captain must be in the starting XI.");
        if (req.ViceCaptainId is not null && req.ViceCaptainId == req.CaptainId) errors.Add("Captain and vice-captain must be different players.");
        foreach (var p in req.Players.Where(p => squad.TryGetValue(p.PlayerId, out var m) && m.Availability != nameof(AvailabilityStatus.Available)))
            warnings.Add($"{squad[p.PlayerId].Name} is {squad[p.PlayerId].Availability}.");

        var rated = await ratings.RateManyAsync(starterIds.ToList(), ct);
        var strength = starters.Count == 0 ? 0 : Math.Round(starters.Average(p => rated.TryGetValue(p.PlayerId, out var x) ? x.Rating : 40m), 1);
        return new XIValidationDto(errors, warnings, overseas, bowling, strength);
    }

    private async Task<PlayingXIDto> ToDtoAsync(PlayingXI xi, Guid fid, int seasonId, CancellationToken ct)
    {
        var ids = xi.Players.Select(p => p.PlayerId).ToList();
        var players = await db.Players.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, Role = p.PrimaryRole!.Name, p.IsOverseas, p.IsWicketKeeper }).ToDictionaryAsync(p => p.Id, ct);
        var avail = await db.PlayerAvailability.AsNoTracking().Where(a => ids.Contains(a.PlayerId)).ToDictionaryAsync(a => a.PlayerId, a => a.Status.ToString(), ct);
        var req = new SavePlayingXIRequest(xi.Name, xi.Opponent, xi.Venue, xi.CaptainId, xi.ViceCaptainId, xi.WicketKeeperId, xi.IsPreferred, xi.Notes,
            xi.Players.Select(p => new XIPlayerRequest(p.PlayerId, p.BattingOrder, p.IsImpactSubstitute, p.IsBowlingOption)).ToList());
        var validation = await ValidateInternalAsync(req, fid, seasonId, ct);
        return new PlayingXIDto(xi.Id, xi.Name, xi.Opponent, xi.Venue, xi.CaptainId, xi.ViceCaptainId, xi.WicketKeeperId, xi.IsPreferred, xi.Notes,
            xi.Players.OrderBy(p => p.IsImpactSubstitute).ThenBy(p => p.BattingOrder)
                .Select(p => new XIPlayerDto(p.PlayerId, players.TryGetValue(p.PlayerId, out var pl) ? pl.FullName : "Player no longer available",
                    pl?.Role ?? "", pl?.IsOverseas ?? false, pl?.IsWicketKeeper ?? false, p.BattingOrder, p.IsImpactSubstitute, p.IsBowlingOption,
                    avail.GetValueOrDefault(p.PlayerId, "Available"))).ToList(),
            validation.Errors.Concat(validation.Warnings).ToList(), xi.UpdatedAt ?? xi.CreatedAt);
    }
}
