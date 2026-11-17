using Ipl.Application.Rules;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Players;

public record PlayerFranchiseDto(Guid Id, string Name, string ShortName, string PrimaryColor);

public record PlayerListItemDto(
    Guid Id, string Code, string FullName, string DisplayName, int Age, string Country, string CountryCode, bool IsOverseas,
    bool IsCapped, string Role, string RoleCode, string? SecondaryRole, string BattingHand, string BowlingStyle, string BowlingType,
    bool IsWicketKeeper, PlayerFranchiseDto? Franchise, string Status, string Availability, decimal? CurrentPrice,
    int T20Matches, int T20Runs, int T20Wickets, decimal? T20StrikeRate, decimal? T20Economy, string? PhotoUrl, bool IsDemo);

public record PlayerSearchRequest(
    string? Search = null, int? CountryId = null, string? Category = null, string? BattingHand = null, string? BowlingType = null,
    int? AgeMin = null, int? AgeMax = null, Guid? FranchiseId = null, bool? Overseas = null, bool? Capped = null,
    string? Status = null, string? Availability = null, decimal? PriceMin = null, decimal? PriceMax = null,
    int? MinRuns = null, int? MinWickets = null, decimal? MinStrikeRate = null, decimal? MaxEconomy = null, bool? FreeAgentsOnly = null);

public record PlayerTeamHistoryDto(int Year, string Franchise, string Acquisition, decimal? Price);

public record AvailabilityDto(string Status, string? PublicNote, DateOnly? ExpectedReturn, DateTime UpdatedAt);

public record PlayerDetailDto(
    Guid Id, string Code, string FullName, string DisplayName, DateOnly DateOfBirth, int Age, int CountryId, string Country,
    string CountryCode, string? PhotoUrl, string BattingHand, string BowlingStyle, string BowlingType, int PrimaryRoleId, string Role,
    string RoleCode, int? SecondaryRoleId, string? SecondaryRole, bool IsWicketKeeper, bool IsOverseas, bool IsCapped,
    PlayerFranchiseDto? Franchise, string Status, string CareerStatus, AvailabilityDto Availability,
    IReadOnlyList<PlayerTeamHistoryDto> TeamHistory, decimal? CurrentPrice, bool IsDemo, string DataSource, DateTime? UpdatedAt);

public record SavePlayerRequest(
    string FullName, string DisplayName, DateOnly DateOfBirth, int CountryId, string? PhotoUrl, BattingHand BattingHand,
    string BowlingStyle, BowlingType BowlingType, int PrimaryRoleId, int? SecondaryRoleId, bool IsWicketKeeper, bool IsCapped,
    PlayerStatus Status, CareerStatus CareerStatus, string DataSource);

public record LookupDto(int Id, string Code, string Name, string? Group = null);

public record ImportResult(int Created, int Updated, int Skipped, IReadOnlyList<string> Errors);

public class PlayerService(IAppDbContext db, ICurrentUser me, IAuditService audit, IRulesService rules, IClock clock, IAppCache cache)
{
    /// <summary>Category names from the spec mapped to role codes / bowling types.</summary>
    public static readonly string[] Categories =
        ["Batsman", "Bowler", "All-rounder", "Wicketkeeper", "Wicketkeeper-batsman", "Fast bowler", "Medium-fast bowler",
         "Spin bowler", "Leg spinner", "Off spinner", "Left-arm spinner"];

    public async Task<object> LookupsAsync(CancellationToken ct)
    {
        me.Require(Perm.PlayersRead);
        return await cache.GetOrCreateAsync("lookups:players", async () => new
        {
            Countries = await db.Countries.AsNoTracking().OrderBy(c => c.Name).Select(c => new LookupDto(c.Id, c.Code, c.Name, c.IsHomeCountry ? "home" : null)).ToListAsync(ct),
            Roles = await db.PlayerRoles.AsNoTracking().OrderBy(r => r.SortOrder).Select(r => new LookupDto(r.Id, r.Code, r.Name, r.Group)).ToListAsync(ct),
            Competitions = await db.Competitions.AsNoTracking().OrderBy(c => c.Type).ThenByDescending(c => c.Level)
                .Select(c => new LookupDto(c.Id, c.Code, c.Name, c.Type.ToString() + "|" + c.Format.ToString())).ToListAsync(ct),
            Categories,
            BowlingTypes = Enum.GetNames<BowlingType>(),
            Statuses = Enum.GetNames<PlayerStatus>(),
            AvailabilityStatuses = Enum.GetNames<AvailabilityStatus>(),
        }, TimeSpan.FromMinutes(30), ct);
    }

    public async Task<PagedResult<PlayerListItemDto>> SearchAsync(PlayerSearchRequest f, PageRequest page, CancellationToken ct)
    {
        me.Require(Perm.PlayersRead);
        var season = await rules.CurrentSeasonAsync(ct);
        var today = clock.Today;
        var t20 = await PrimaryT20CompetitionIdAsync(ct);

        var q = db.Players.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(p => p.FullName.Contains(s) || p.DisplayName.Contains(s) || p.Code == s);
        }
        if (f.CountryId is { } c) q = q.Where(p => p.CountryId == c);
        if (f.FranchiseId is { } fid) q = q.Where(p => p.CurrentFranchiseId == fid);
        if (f.FreeAgentsOnly == true) q = q.Where(p => p.CurrentFranchiseId == null);
        if (f.Overseas is { } o) q = q.Where(p => p.IsOverseas == o);
        if (f.Capped is { } cap) q = q.Where(p => p.IsCapped == cap);
        if (Enum.TryParse<BattingHand>(f.BattingHand, true, out var hand)) q = q.Where(p => p.BattingHand == hand);
        if (Enum.TryParse<BowlingType>(f.BowlingType, true, out var bt)) q = q.Where(p => p.BowlingType == bt);
        if (Enum.TryParse<PlayerStatus>(f.Status, true, out var st)) q = q.Where(p => p.Status == st);
        if (Enum.TryParse<AvailabilityStatus>(f.Availability, true, out var av))
            q = av == AvailabilityStatus.Available
                ? q.Where(p => !db.PlayerAvailability.Any(a => a.PlayerId == p.Id) || db.PlayerAvailability.Any(a => a.PlayerId == p.Id && a.Status == av))
                : q.Where(p => db.PlayerAvailability.Any(a => a.PlayerId == p.Id && a.Status == av));
        if (f.AgeMin is { } amin) { var born = today.AddYears(-amin); q = q.Where(p => p.DateOfBirth <= born); }
        if (f.AgeMax is { } amax) { var born = today.AddYears(-amax - 1); q = q.Where(p => p.DateOfBirth > born); }
        q = ApplyCategory(q, f.Category);

        var projected = q.Select(p => new
        {
            P = p,
            Country = p.Country!.Name,
            CountryCode = p.Country.Code,
            Role = p.PrimaryRole!.Name,
            RoleCode = p.PrimaryRole.Code,
            Secondary = p.SecondaryRole != null ? p.SecondaryRole.Name : null,
            Fr = p.CurrentFranchise,
            Avail = db.PlayerAvailability.Where(a => a.PlayerId == p.Id).Select(a => (AvailabilityStatus?)a.Status).FirstOrDefault(),
            Price = db.FranchiseSquads.Where(s => s.PlayerId == p.Id && s.SeasonId == season.Id && s.Status == SquadMemberStatus.Active)
                .Select(s => (decimal?)s.Price).FirstOrDefault(),
            Career = db.PlayerCareerStatistics.Where(cs => cs.PlayerId == p.Id && cs.CompetitionId == t20).FirstOrDefault(),
        });
        if (f.PriceMin is { } pmin) projected = projected.Where(x => x.Price >= pmin);
        if (f.PriceMax is { } pmax) projected = projected.Where(x => x.Price <= pmax);
        if (f.MinRuns is { } mr) projected = projected.Where(x => x.Career != null && x.Career.Runs >= mr);
        if (f.MinWickets is { } mw) projected = projected.Where(x => x.Career != null && x.Career.Wickets >= mw);
        if (f.MinStrikeRate is { } msr)
            projected = projected.Where(x => x.Career != null && x.Career.BallsFaced > 0 && x.Career.Runs * 100m / x.Career.BallsFaced >= msr);
        if (f.MaxEconomy is { } mec)
            projected = projected.Where(x => x.Career != null && x.Career.BallsBowled > 0 && x.Career.RunsConceded * 6m / x.Career.BallsBowled <= mec);

        projected = (page.Sort?.ToLowerInvariant(), page.Desc) switch
        {
            ("age", false) => projected.OrderByDescending(x => x.P.DateOfBirth),
            ("age", true) => projected.OrderBy(x => x.P.DateOfBirth),
            ("runs", false) => projected.OrderBy(x => x.Career!.Runs),
            ("runs", true) => projected.OrderByDescending(x => x.Career!.Runs),
            ("wickets", false) => projected.OrderBy(x => x.Career!.Wickets),
            ("wickets", true) => projected.OrderByDescending(x => x.Career!.Wickets),
            ("price", false) => projected.OrderBy(x => x.Price),
            ("price", true) => projected.OrderByDescending(x => x.Price),
            ("country", _) => page.Desc ? projected.OrderByDescending(x => x.Country) : projected.OrderBy(x => x.Country),
            (_, true) => projected.OrderByDescending(x => x.P.FullName),
            _ => projected.OrderBy(x => x.P.FullName),
        };

        var total = await projected.CountAsync(ct);
        var rows = await projected.Skip((page.SafePage - 1) * page.SafeSize).Take(page.SafeSize).ToListAsync(ct);
        var items = rows.Select(x => new PlayerListItemDto(
            x.P.Id, x.P.Code, x.P.FullName, x.P.DisplayName, AgeOn(x.P.DateOfBirth, today), x.Country, x.CountryCode, x.P.IsOverseas,
            x.P.IsCapped, x.Role, x.RoleCode, x.Secondary, x.P.BattingHand.ToString(), x.P.BowlingStyle, x.P.BowlingType.ToString(),
            x.P.IsWicketKeeper, x.Fr is null ? null : new PlayerFranchiseDto(x.Fr.Id, x.Fr.Name, x.Fr.ShortName, x.Fr.PrimaryColor),
            x.P.Status.ToString(), (x.Avail ?? AvailabilityStatus.Available).ToString(), x.Price,
            x.Career?.Matches ?? 0, x.Career?.Runs ?? 0, x.Career?.Wickets ?? 0,
            x.Career is { BallsFaced: > 0 } b ? Math.Round(b.Runs * 100m / b.BallsFaced, 2) : null,
            x.Career is { BallsBowled: > 0 } w ? Math.Round(w.RunsConceded * 6m / w.BallsBowled, 2) : null,
            x.P.PhotoUrl, x.P.IsDemo)).ToList();
        return new PagedResult<PlayerListItemDto>(items, page.SafePage, page.SafeSize, total);
    }

    public async Task<PlayerDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        me.Require(Perm.PlayersRead);
        var season = await rules.CurrentSeasonAsync(ct);
        var p = await db.Players.AsNoTracking().Include(x => x.Country).Include(x => x.PrimaryRole).Include(x => x.SecondaryRole)
            .Include(x => x.CurrentFranchise).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Player");
        var avail = await db.PlayerAvailability.AsNoTracking().FirstOrDefaultAsync(a => a.PlayerId == id, ct);
        var history = await db.PlayerTeams.AsNoTracking().Where(t => t.PlayerId == id).OrderByDescending(t => t.Season!.Year)
            .Select(t => new PlayerTeamHistoryDto(t.Season!.Year, t.Franchise!.Name, t.Acquisition.ToString(), t.Price)).ToListAsync(ct);
        var price = await db.FranchiseSquads.AsNoTracking()
            .Where(s => s.PlayerId == id && s.SeasonId == season.Id && s.Status == SquadMemberStatus.Active).Select(s => (decimal?)s.Price).FirstOrDefaultAsync(ct);
        return new PlayerDetailDto(p.Id, p.Code, p.FullName, p.DisplayName, p.DateOfBirth, AgeOn(p.DateOfBirth, clock.Today), p.CountryId,
            p.Country!.Name, p.Country.Code, p.PhotoUrl, p.BattingHand.ToString(), p.BowlingStyle, p.BowlingType.ToString(), p.PrimaryRoleId,
            p.PrimaryRole!.Name, p.PrimaryRole.Code, p.SecondaryRoleId, p.SecondaryRole?.Name, p.IsWicketKeeper, p.IsOverseas, p.IsCapped,
            p.CurrentFranchise is null ? null : new PlayerFranchiseDto(p.CurrentFranchise.Id, p.CurrentFranchise.Name, p.CurrentFranchise.ShortName, p.CurrentFranchise.PrimaryColor),
            p.Status.ToString(), p.CareerStatus.ToString(),
            avail is null ? new AvailabilityDto(nameof(AvailabilityStatus.Available), null, null, p.CreatedAt)
                : new AvailabilityDto(avail.Status.ToString(), avail.PublicNote, avail.ExpectedReturn, avail.UpdatedAt),
            history, price, p.IsDemo, p.DataSource, p.UpdatedAt ?? p.CreatedAt);
    }

    public async Task<Guid> CreateAsync(SavePlayerRequest req, CancellationToken ct)
    {
        me.Require(Perm.PlayersManage);
        var player = new Player { Code = await NextCodeAsync(ct) };
        await ApplyAsync(player, req, ct);
        db.Players.Add(player);
        audit.Record("players.created", "Player", player.Id, new { player.FullName, player.Code });
        await db.SaveChangesAsync(ct);
        return player.Id;
    }

    public async Task UpdateAsync(Guid id, SavePlayerRequest req, CancellationToken ct)
    {
        me.Require(Perm.PlayersManage);
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Player");
        await ApplyAsync(player, req, ct);
        audit.Record("players.updated", "Player", id, req);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        me.Require(Perm.PlayersManage);
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Player");
        if (player.CurrentFranchiseId is not null) throw new ConflictException("Release the player from their franchise before deleting.");
        db.Players.Remove(player); // soft delete
        audit.Record("players.deleted", "Player", id, new { player.FullName });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Imports or updates players from CSV. Columns: code (optional), full_name, display_name, date_of_birth (yyyy-mm-dd),
    /// country_code, batting_hand (RightHand/LeftHand), bowling_style, bowling_type, role_code, secondary_role_code,
    /// is_wicketkeeper, is_capped, source.
    /// </summary>
    public async Task<ImportResult> ImportCsvAsync(Stream csv, CancellationToken ct)
    {
        me.Require(Perm.PlayersManage);
        var rows = Csv.Read(csv);
        var countries = await db.Countries.ToDictionaryAsync(c => c.Code.ToUpperInvariant(), ct);
        var roles = await db.PlayerRoles.ToDictionaryAsync(r => r.Code.ToLowerInvariant(), ct);
        int created = 0, updated = 0, skipped = 0;
        List<string> errors = [];
        var nextNumber = await NextCodeNumberAsync(ct);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var line = i + 2;
            try
            {
                var source = r.Get("source");
                if (string.IsNullOrWhiteSpace(source)) throw new FormatException("source is required (where the data comes from).");
                if (!countries.TryGetValue(r.Get("country_code").ToUpperInvariant(), out var country)) throw new FormatException($"unknown country '{r.Get("country_code")}'.");
                if (!roles.TryGetValue(r.Get("role_code").ToLowerInvariant(), out var role)) throw new FormatException($"unknown role '{r.Get("role_code")}'.");
                var secondary = r.Get("secondary_role_code") is { Length: > 0 } sc
                    ? roles.TryGetValue(sc.ToLowerInvariant(), out var sr) ? sr : throw new FormatException($"unknown role '{sc}'.") : null;
                var req = new SavePlayerRequest(
                    r.Get("full_name"), r.Get("display_name") is { Length: > 0 } dn ? dn : r.Get("full_name"),
                    DateOnly.ParseExact(r.Get("date_of_birth"), "yyyy-MM-dd"), country.Id, null,
                    Enum.Parse<BattingHand>(r.Get("batting_hand"), true), r.Get("bowling_style"),
                    r.Get("bowling_type") is { Length: > 0 } btv ? Enum.Parse<BowlingType>(btv, true) : BowlingType.None,
                    role.Id, secondary?.Id, Csv.Bool(r.Get("is_wicketkeeper")), Csv.Bool(r.Get("is_capped")),
                    PlayerStatus.Active, CareerStatus.Active, source);
                var code = r.Get("code");
                var existing = code.Length > 0 ? await db.Players.FirstOrDefaultAsync(p => p.Code == code, ct) : null;
                if (existing is not null)
                {
                    req = req with { Status = existing.Status, CareerStatus = existing.CareerStatus, PhotoUrl = existing.PhotoUrl };
                    await ApplyAsync(existing, req, ct);
                    updated++;
                }
                else
                {
                    var player = new Player { Code = code.Length > 0 ? code : $"P{nextNumber++:00000}" };
                    await ApplyAsync(player, req, ct);
                    db.Players.Add(player);
                    created++;
                }
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or AppValidationException or KeyNotFoundException)
            {
                skipped++;
                errors.Add($"Line {line}: {ex.Message}");
            }
        }
        audit.Record("players.imported", "Player", null, new { created, updated, skipped });
        await db.SaveChangesAsync(ct);
        await cache.BumpAsync("stats", ct);
        return new ImportResult(created, updated, skipped, errors.Take(50).ToList());
    }

    public async Task SetAvailabilityAsync(Guid playerId, AvailabilityStatus status, string? publicNote, DateOnly? expectedReturn, CancellationToken ct)
    {
        me.Require(Perm.AvailabilityManage);
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct) ?? throw new NotFoundException("Player");
        if (!me.IsLeagueWide && player.CurrentFranchiseId != me.FranchiseId) throw new ForbiddenException("You can only update availability for your own players.");
        var a = await db.PlayerAvailability.FirstOrDefaultAsync(x => x.PlayerId == playerId, ct);
        if (a is null) db.PlayerAvailability.Add(a = new PlayerAvailability { PlayerId = playerId });
        var before = a.Status;
        a.Status = status;
        a.PublicNote = publicNote?.Trim();
        a.ExpectedReturn = expectedReturn;
        a.UpdatedAt = clock.UtcNow;
        a.UpdatedBy = me.UserId;
        if (status == AvailabilityStatus.Injured && player.Status is PlayerStatus.Active or PlayerStatus.UnderContract or PlayerStatus.Retained)
            player.Status = PlayerStatus.Injured;
        else if (status == AvailabilityStatus.Available && player.Status == PlayerStatus.Injured)
            player.Status = player.CurrentFranchiseId is null ? PlayerStatus.Active : PlayerStatus.UnderContract;
        audit.Record("availability.updated", "Player", playerId, new { from = before.ToString(), to = status.ToString(), publicNote }, player.CurrentFranchiseId);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------

    public static int AgeOn(DateOnly dob, DateOnly today)
    {
        var age = today.Year - dob.Year;
        if (dob > today.AddYears(-age)) age--;
        return age;
    }

    /// <summary>The franchise T20 competition used for headline numbers (highest-level franchise T20).</summary>
    public async Task<int> PrimaryT20CompetitionIdAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync("lookups:primary-t20", async () =>
            await db.Competitions.Where(c => c.Type == CompetitionType.Franchise && c.Format == MatchFormat.T20)
                .OrderByDescending(c => c.Level).Select(c => c.Id).FirstOrDefaultAsync(ct), TimeSpan.FromHours(1), ct);

    public static IQueryable<Player> ApplyCategory(IQueryable<Player> q, string? category) => category?.ToLowerInvariant() switch
    {
        "batsman" => q.Where(p => p.PrimaryRole!.Code == "batter"),
        "bowler" => q.Where(p => p.PrimaryRole!.Code == "bowler"),
        "all-rounder" => q.Where(p => p.PrimaryRole!.Code == "allrounder"),
        "wicketkeeper" => q.Where(p => p.IsWicketKeeper),
        "wicketkeeper-batsman" => q.Where(p => p.PrimaryRole!.Code == "wk_batter"),
        "fast bowler" => q.Where(p => p.BowlingType == BowlingType.Fast),
        "medium-fast bowler" => q.Where(p => p.BowlingType == BowlingType.MediumFast || p.BowlingType == BowlingType.Medium),
        "spin bowler" => q.Where(p => p.BowlingType == BowlingType.OffSpin || p.BowlingType == BowlingType.LegSpin
            || p.BowlingType == BowlingType.LeftArmOrthodox || p.BowlingType == BowlingType.LeftArmWristSpin),
        "leg spinner" => q.Where(p => p.BowlingType == BowlingType.LegSpin),
        "off spinner" => q.Where(p => p.BowlingType == BowlingType.OffSpin),
        "left-arm spinner" => q.Where(p => p.BowlingType == BowlingType.LeftArmOrthodox || p.BowlingType == BowlingType.LeftArmWristSpin),
        _ => q,
    };

    private async Task ApplyAsync(Player p, SavePlayerRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.FullName)) throw new AppValidationException("fullName", "Enter the player's full name.");
        if (string.IsNullOrWhiteSpace(r.DataSource)) throw new AppValidationException("dataSource", "Record where this player's data comes from.");
        if (r.DateOfBirth > clock.Today.AddYears(-14) || r.DateOfBirth < new DateOnly(1950, 1, 1))
            throw new AppValidationException("dateOfBirth", "Enter a realistic date of birth.");
        var country = await db.Countries.FirstOrDefaultAsync(c => c.Id == r.CountryId, ct) ?? throw new AppValidationException("countryId", "Unknown country.");
        if (!await db.PlayerRoles.AnyAsync(x => x.Id == r.PrimaryRoleId, ct)) throw new AppValidationException("primaryRoleId", "Unknown role.");
        if (r.PhotoUrl is { Length: > 0 } url && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps))
            throw new AppValidationException("photoUrl", "The photo must be an https:// address you have the rights to use.");
        p.FullName = r.FullName.Trim();
        p.DisplayName = string.IsNullOrWhiteSpace(r.DisplayName) ? p.FullName : r.DisplayName.Trim();
        p.DateOfBirth = r.DateOfBirth;
        p.CountryId = country.Id;
        p.IsOverseas = !country.IsHomeCountry;
        p.PhotoUrl = string.IsNullOrWhiteSpace(r.PhotoUrl) ? null : r.PhotoUrl.Trim();
        p.BattingHand = r.BattingHand;
        p.BowlingStyle = r.BowlingStyle?.Trim() ?? "";
        p.BowlingType = r.BowlingType;
        p.PrimaryRoleId = r.PrimaryRoleId;
        p.SecondaryRoleId = r.SecondaryRoleId;
        p.IsWicketKeeper = r.IsWicketKeeper;
        p.IsCapped = r.IsCapped;
        p.Status = r.Status;
        p.CareerStatus = r.CareerStatus;
        p.DataSource = r.DataSource.Trim();
    }

    private async Task<string> NextCodeAsync(CancellationToken ct) => $"P{await NextCodeNumberAsync(ct):00000}";

    private async Task<int> NextCodeNumberAsync(CancellationToken ct)
    {
        var codes = await db.Players.IgnoreQueryFilters().Where(p => p.Code.StartsWith("P")).Select(p => p.Code).ToListAsync(ct);
        return codes.Select(c => int.TryParse(c[1..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
    }
}

/// <summary>Minimal RFC 4180 CSV reader (quoted fields, commas and newlines inside quotes).</summary>
public static class Csv
{
    public sealed class Row(Dictionary<string, string> values)
    {
        public string Get(string column) => values.TryGetValue(column, out var v) ? v.Trim() : "";
        public int Int(string column) => string.IsNullOrWhiteSpace(Get(column)) ? 0 : int.Parse(Get(column), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool Bool(string v) => v.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "y";

    public static List<Row> Read(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var records = Parse(text);
        if (records.Count == 0) return [];
        var header = records[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        return records.Skip(1).Where(r => r.Any(v => v.Length > 0))
            .Select(r => new Row(header.Select((h, i) => (h, v: i < r.Count ? r[i] : "")).ToDictionary(x => x.h, x => x.v)))
            .ToList();
    }

    private static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = [];
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    public static string Escape(object? value)
    {
        var s = value switch
        {
            null => "",
            DateTime d => d.ToString("yyyy-MM-dd HH:mm"),
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        // Neutralise spreadsheet formula injection.
        if (s.Length > 0 && "=+-@".Contains(s[0]) && !decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
            s = "'" + s;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}
