using Ipl.Application.Rules;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Franchises;

public record FranchiseListItemDto(
    Guid Id, string Code, string Name, string ShortName, string City, string PrimaryColor, string SecondaryColor,
    string? LogoUrl, string OwnerName, string HomeGround, bool IsActive, bool IsDemo, int Titles, int SquadSize, decimal? PurseRemaining);

public record FranchiseSeasonDto(
    int SeasonId, int Year, string SeasonName, int? FinalPosition, int Played, int Won, int Lost, int NoResult, int Points,
    decimal? NetRunRate, bool IsChampion, bool IsRunnerUp, decimal? PurseTotal, decimal? PurseRemaining);

public record StaffDto(Guid Id, string Name, string Position, string Category, int? SinceYear, bool IsPlatformUser);

public record MemberDto(Guid UserId, string FullName, string Email, string JobTitle, IReadOnlyList<string> Roles, bool IsActive);

public record FranchiseDetailDto(
    Guid Id, string Code, string Name, string ShortName, string City, string? LogoUrl, string PrimaryColor, string SecondaryColor,
    string OwnerName, string? OwnerCompany, string? OwnerEmail, string HomeGround, int? FoundedYear, bool IsActive, bool IsDemo,
    bool CanViewConfidential, IReadOnlyList<StaffDto> Staff, IReadOnlyList<MemberDto> Members, IReadOnlyList<FranchiseSeasonDto> History,
    FranchiseSeasonDto? CurrentSeason, int Championships, int Playoffs);

public record FranchiseActivityDto(DateTime At, string? UserEmail, string Action, string EntityType, string? EntityId, string? Details);

public record SaveFranchiseRequest(
    string Code, string Name, string ShortName, string City, string PrimaryColor, string SecondaryColor, string OwnerName,
    string? OwnerCompany, string? OwnerEmail, string HomeGround, int? FoundedYear, string? LogoUrl);

public record SaveStaffRequest(string Name, string Position, StaffCategory Category, int? SinceYear);

public class FranchiseService(IAppDbContext db, ICurrentUser me, IAuditService audit, IRulesService rules, IClock clock)
{
    public async Task<IReadOnlyList<FranchiseListItemDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        me.Require(Perm.FranchisesRead);
        var season = await rules.CurrentSeasonAsync(ct);
        var rows = await db.Franchises.AsNoTracking()
            .Where(f => includeInactive || f.IsActive)
            .OrderBy(f => f.Name)
            .Select(f => new
            {
                f, Titles = f.Seasons.Count(s => s.IsChampion),
                Squad = db.FranchiseSquads.Count(s => s.FranchiseId == f.Id && s.SeasonId == season.Id && s.Status == SquadMemberStatus.Active),
                Purse = f.Seasons.Where(s => s.SeasonId == season.Id).Select(s => (decimal?)s.PurseRemaining).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows.Select(r => new FranchiseListItemDto(r.f.Id, r.f.Code, r.f.Name, r.f.ShortName, r.f.City, r.f.PrimaryColor, r.f.SecondaryColor,
            r.f.LogoUrl, r.f.OwnerName, r.f.HomeGround, r.f.IsActive, r.f.IsDemo, r.Titles, r.Squad, r.Purse)).ToList();
    }

    public async Task<FranchiseDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        me.Require(Perm.FranchisesRead);
        var f = await db.Franchises.AsNoTracking().Include(x => x.Staff).Include(x => x.Seasons).ThenInclude(s => s.Season)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Franchise");
        var confidential = me.CanAccessFranchise(id);
        var current = await rules.CurrentSeasonAsync(ct);

        var members = await db.FranchiseMembers.AsNoTracking().Where(m => m.FranchiseId == id)
            .Select(m => new
            {
                m.UserId, m.User!.FullName, m.User.Email, m.JobTitle, m.User.IsActive,
                Roles = db.UserRoles.Where(r => r.UserId == m.UserId && r.FranchiseId == id).Select(r => r.Role!.Name).ToList(),
            })
            .OrderBy(m => m.FullName).ToListAsync(ct);

        FranchiseSeasonDto Map(FranchiseSeason s) => new(s.SeasonId, s.Season!.Year, s.Season.Name, s.FinalPosition, s.Played, s.Won, s.Lost,
            s.NoResult, s.Points, s.NetRunRate, s.IsChampion, s.IsRunnerUp, confidential ? s.PurseTotal : null, confidential ? s.PurseRemaining : null);

        var history = f.Seasons.OrderByDescending(s => s.Season!.Year).Select(Map).ToList();
        return new FranchiseDetailDto(f.Id, f.Code, f.Name, f.ShortName, f.City, f.LogoUrl, f.PrimaryColor, f.SecondaryColor, f.OwnerName,
            f.OwnerCompany, confidential ? f.OwnerEmail : null, f.HomeGround, f.FoundedYear, f.IsActive, f.IsDemo, confidential,
            f.Staff.OrderBy(s => s.Category).ThenBy(s => s.Name)
                .Select(s => new StaffDto(s.Id, s.Name, s.Position, s.Category.ToString(), s.SinceYear, s.UserId != null)).ToList(),
            confidential ? members.Select(m => new MemberDto(m.UserId, m.FullName, m.Email, m.JobTitle, m.Roles, m.IsActive)).ToList() : [],
            history, history.FirstOrDefault(h => h.SeasonId == current.Id),
            f.Seasons.Count(s => s.IsChampion), f.Seasons.Count(s => s.FinalPosition is <= 4));
    }

    public async Task<PagedResult<FranchiseActivityDto>> ActivityAsync(Guid id, PageRequest page, CancellationToken ct)
    {
        me.Require(Perm.AuditRead);
        me.RequireFranchiseAccess(id);
        return await db.AuditLogs.AsNoTracking().Where(a => a.FranchiseId == id).OrderByDescending(a => a.At)
            .Select(a => new FranchiseActivityDto(a.At, a.UserEmail, a.Action, a.EntityType, a.EntityId, a.Details))
            .ToPagedAsync(page, ct);
    }

    public async Task<Guid> CreateAsync(SaveFranchiseRequest req, CancellationToken ct)
    {
        me.Require(Perm.FranchisesManage);
        Validate(req);
        var code = req.Code.Trim().ToUpperInvariant();
        if (await db.Franchises.IgnoreQueryFilters().AnyAsync(f => f.Code == code, ct)) throw new ConflictException($"Franchise code {code} is already used.");
        var f = new Franchise();
        Apply(f, req);
        db.Franchises.Add(f);

        // Give the new franchise a purse for the current season according to that season's rules.
        var season = await rules.CurrentSeasonAsync(ct);
        var seasonRules = await rules.ForSeasonAsync(season.Id, ct);
        var purse = seasonRules.Decimal(RuleKeys.AuctionPurse);
        db.FranchiseSeasons.Add(new FranchiseSeason { FranchiseId = f.Id, SeasonId = season.Id, PurseTotal = purse, PurseRemaining = purse });
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            FranchiseId = f.Id, SeasonId = season.Id, Category = FinanceCategory.AuctionPurse, Direction = FinanceDirection.Credit,
            Amount = purse, PurseBalanceAfter = purse, Description = $"{season.Name} auction purse allocation",
            IdempotencyKey = $"purse-alloc:{f.Id}:{season.Id}", CreatedAt = clock.UtcNow, CreatedBy = me.UserId,
        });
        audit.Record("franchises.created", "Franchise", f.Id, new { f.Code, f.Name }, f.Id);
        await db.SaveChangesAsync(ct);
        return f.Id;
    }

    public async Task UpdateAsync(Guid id, SaveFranchiseRequest req, CancellationToken ct)
    {
        RequireEdit(id);
        Validate(req);
        var f = await db.Franchises.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Franchise");
        var code = req.Code.Trim().ToUpperInvariant();
        if (code != f.Code)
        {
            if (!me.Has(Perm.FranchisesManage)) throw new ForbiddenException("Only the league can change a franchise code.");
            if (await db.Franchises.IgnoreQueryFilters().AnyAsync(x => x.Code == code && x.Id != id, ct)) throw new ConflictException($"Franchise code {code} is already used.");
        }
        Apply(f, req);
        audit.Record("franchises.updated", "Franchise", id, req, id);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        me.Require(Perm.FranchisesManage);
        var f = await db.Franchises.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Franchise");
        f.IsActive = active;
        audit.Record(active ? "franchises.activated" : "franchises.deactivated", "Franchise", id, null, id);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> AddStaffAsync(Guid franchiseId, SaveStaffRequest req, CancellationToken ct)
    {
        RequireEdit(franchiseId);
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Position)) throw new AppValidationException("name", "Enter a name and position.");
        var staff = new FranchiseStaff { FranchiseId = franchiseId, Name = req.Name.Trim(), Position = req.Position.Trim(), Category = req.Category, SinceYear = req.SinceYear };
        db.FranchiseStaff.Add(staff);
        audit.Record("franchises.staff_added", "FranchiseStaff", staff.Id, req, franchiseId);
        await db.SaveChangesAsync(ct);
        return staff.Id;
    }

    public async Task RemoveStaffAsync(Guid franchiseId, Guid staffId, CancellationToken ct)
    {
        RequireEdit(franchiseId);
        var staff = await db.FranchiseStaff.FirstOrDefaultAsync(s => s.Id == staffId && s.FranchiseId == franchiseId, ct) ?? throw new NotFoundException("Staff member");
        db.FranchiseStaff.Remove(staff);
        audit.Record("franchises.staff_removed", "FranchiseStaff", staffId, new { staff.Name, staff.Position }, franchiseId);
        await db.SaveChangesAsync(ct);
    }

    private void RequireEdit(Guid franchiseId)
    {
        if (me.Has(Perm.FranchisesManage)) return;
        me.Require(Perm.FranchisesEdit);
        me.RequireFranchiseAccess(franchiseId);
    }

    private static void Validate(SaveFranchiseRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Code) || r.Code.Trim().Length is < 2 or > 5) throw new AppValidationException("code", "Code must be 2–5 letters.");
        if (string.IsNullOrWhiteSpace(r.Name)) throw new AppValidationException("name", "Enter the franchise name.");
        if (string.IsNullOrWhiteSpace(r.ShortName)) throw new AppValidationException("shortName", "Enter a short name.");
        if (string.IsNullOrWhiteSpace(r.City)) throw new AppValidationException("city", "Enter the city.");
        if (string.IsNullOrWhiteSpace(r.OwnerName)) throw new AppValidationException("ownerName", "Enter the owner.");
        if (string.IsNullOrWhiteSpace(r.HomeGround)) throw new AppValidationException("homeGround", "Enter the home ground.");
        foreach (var (field, color) in new[] { ("primaryColor", r.PrimaryColor), ("secondaryColor", r.SecondaryColor) })
            if (!System.Text.RegularExpressions.Regex.IsMatch(color ?? "", "^#[0-9a-fA-F]{6}$")) throw new AppValidationException(field, "Use a colour like #1d4ed8.");
        if (r.LogoUrl is { Length: > 0 } url && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps))
            throw new AppValidationException("logoUrl", "The logo must be an https:// address.");
    }

    private static void Apply(Franchise f, SaveFranchiseRequest r)
    {
        f.Code = r.Code.Trim().ToUpperInvariant();
        f.Name = r.Name.Trim();
        f.ShortName = r.ShortName.Trim();
        f.City = r.City.Trim();
        f.PrimaryColor = r.PrimaryColor;
        f.SecondaryColor = r.SecondaryColor;
        f.OwnerName = r.OwnerName.Trim();
        f.OwnerCompany = r.OwnerCompany?.Trim();
        f.OwnerEmail = r.OwnerEmail?.Trim();
        f.HomeGround = r.HomeGround.Trim();
        f.FoundedYear = r.FoundedYear;
        f.LogoUrl = string.IsNullOrWhiteSpace(r.LogoUrl) ? null : r.LogoUrl.Trim();
    }
}
