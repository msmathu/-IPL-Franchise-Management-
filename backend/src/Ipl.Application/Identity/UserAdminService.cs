using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Identity;

public record RoleDto(int Id, string Code, string Name, string Description, string Scope, IReadOnlyList<string> Permissions);

public record PermissionDto(string Code, string Module, string Description);

public record UserRoleDto(string RoleCode, string RoleName, Guid? FranchiseId, string? FranchiseName);

public record UserListItemDto(
    Guid Id, string Email, string FullName, bool IsActive, bool IsActivated, DateTime? LastLoginAt,
    string? JobTitle, IReadOnlyList<UserRoleDto> Roles);

public record InviteUserRequest(string Email, string FullName, Guid? FranchiseId, string JobTitle, IReadOnlyList<string> RoleCodes);

public record InviteResult(Guid UserId, string InvitationLink, DateTime ExpiresAt);

public record UpdateUserRolesRequest(Guid? FranchiseId, IReadOnlyList<string> RoleCodes);

public class UserAdminService(IAppDbContext db, ICurrentUser me, IClock clock, IEmailQueue email, IAuditService audit, AuthOptions options)
{
    public async Task<IReadOnlyList<RoleDto>> RolesAsync(CancellationToken ct) =>
        (await db.Roles.AsNoTracking().OrderBy(r => r.SortOrder)
            .Select(r => new { r.Id, r.Code, r.Name, r.Description, r.Scope, Perms = r.Permissions.Select(p => p.Permission!.Code).ToList() })
            .ToListAsync(ct))
        .Select(r => new RoleDto(r.Id, r.Code, r.Name, r.Description, r.Scope.ToString(), r.Perms.OrderBy(p => p).ToList())).ToList();

    public IReadOnlyList<PermissionDto> Permissions() => Perm.All.Select(p => new PermissionDto(p.Code, p.Module, p.Description)).ToList();

    public async Task<PagedResult<UserListItemDto>> ListAsync(Guid? franchiseId, string? search, PageRequest page, CancellationToken ct)
    {
        var scope = ScopeFranchise(franchiseId);
        var query = db.Users.AsNoTracking().AsQueryable();
        if (scope is { } fid) query = query.Where(u => u.Roles.Any(r => r.FranchiseId == fid));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(u => u.FullName.Contains(s) || u.Email.Contains(s));
        }
        var total = await query.CountAsync(ct);
        var users = await query.OrderBy(u => u.FullName).Skip((page.SafePage - 1) * page.SafeSize).Take(page.SafeSize)
            .Select(u => new
            {
                u.Id, u.Email, u.FullName, u.IsActive, u.IsActivated, u.LastLoginAt,
                Roles = u.Roles.Select(r => new UserRoleDto(r.Role!.Code, r.Role.Name, r.FranchiseId, r.Franchise != null ? r.Franchise.Name : null)).ToList(),
            })
            .ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();
        var titles = await db.FranchiseMembers.AsNoTracking().Where(m => ids.Contains(m.UserId) && (scope == null || m.FranchiseId == scope))
            .GroupBy(m => m.UserId).Select(g => new { g.Key, Title = g.Select(m => m.JobTitle).First() }).ToDictionaryAsync(x => x.Key, x => x.Title, ct);
        return new PagedResult<UserListItemDto>(users.Select(u => new UserListItemDto(u.Id, u.Email, u.FullName, u.IsActive, u.IsActivated,
            u.LastLoginAt, titles.GetValueOrDefault(u.Id), u.Roles)).ToList(), page.SafePage, page.SafeSize, total);
    }

    public async Task<InviteResult> InviteAsync(InviteUserRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@')) throw new AppValidationException("email", "Enter a valid e-mail address.");
        if (string.IsNullOrWhiteSpace(req.FullName)) throw new AppValidationException("fullName", "Enter the person's name.");
        if (req.RoleCodes.Count == 0) throw new AppValidationException("roleCodes", "Choose at least one role.");
        var roles = await ResolveAssignableRolesAsync(req.FranchiseId, req.RoleCodes, ct);

        var normalized = Secrets.Normalize(req.Email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            throw new ConflictException("A user with this e-mail already exists. Edit their roles instead.");

        var now = clock.UtcNow;
        var user = new User
        {
            Email = req.Email.Trim(), NormalizedEmail = normalized, FullName = req.FullName.Trim(),
            IsActive = true, IsActivated = false, DefaultFranchiseId = req.FranchiseId,
        };
        db.Users.Add(user);
        foreach (var role in roles)
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, FranchiseId = role.Scope == RoleScope.Franchise ? req.FranchiseId : null, AssignedAt = now, AssignedBy = me.UserId });
        if (req.FranchiseId is { } fid)
            db.FranchiseMembers.Add(new FranchiseMember { FranchiseId = fid, UserId = user.Id, JobTitle = req.JobTitle.Trim(), JoinedAt = now });

        var token = Secrets.NewToken();
        var expires = now.AddDays(7);
        db.UserTokens.Add(new UserToken { UserId = user.Id, Purpose = UserTokenPurpose.Invitation, TokenHash = Secrets.Hash(token), CreatedAt = now, ExpiresAt = expires });
        var link = $"{options.AppBaseUrl}/accept-invitation?token={token}";
        email.Enqueue(user.Email, "You have been invited to the Franchise Management Platform",
            $"Hello {user.FullName},\n\n{me.Name} invited you. Set your password within 7 days:\n{link}");
        audit.Record("users.invited", "User", user.Id, new { user.Email, Roles = req.RoleCodes, req.FranchiseId }, req.FranchiseId);
        await db.SaveChangesAsync(ct);
        return new InviteResult(user.Id, link, expires);
    }

    public async Task SetActiveAsync(Guid userId, bool active, CancellationToken ct)
    {
        if (userId == me.UserId) throw new AppValidationException("userId", "You cannot deactivate your own account.");
        var user = await LoadManageableUserAsync(userId, ct);
        user.IsActive = active;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        if (!active)
        {
            await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow).SetProperty(t => t.RevokedReason, "deactivated"), ct);
        }
        audit.Record(active ? "users.activated" : "users.deactivated", "User", userId);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateRolesAsync(Guid userId, UpdateUserRolesRequest req, CancellationToken ct)
    {
        var user = await LoadManageableUserAsync(userId, ct);
        var roles = await ResolveAssignableRolesAsync(req.FranchiseId, req.RoleCodes, ct);
        var existing = await db.UserRoles.Where(r => r.UserId == userId && r.FranchiseId == req.FranchiseId).ToListAsync(ct);
        db.UserRoles.RemoveRange(existing);
        foreach (var role in roles)
            db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id, FranchiseId = req.FranchiseId, AssignedAt = clock.UtcNow, AssignedBy = me.UserId });
        if (req.FranchiseId is { } fid && roles.Count > 0 && !await db.FranchiseMembers.AnyAsync(m => m.UserId == userId && m.FranchiseId == fid, ct))
            db.FranchiseMembers.Add(new FranchiseMember { FranchiseId = fid, UserId = userId, JobTitle = roles[0].Name, JoinedAt = clock.UtcNow });
        user.SecurityStamp = Guid.NewGuid().ToString("N"); // existing access tokens stop working
        audit.Record("users.roles_changed", "User", userId, new { req.FranchiseId, req.RoleCodes }, req.FranchiseId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LoginHistoryDto>> LoginHistoryAsync(Guid userId, CancellationToken ct)
    {
        await LoadManageableUserAsync(userId, ct);
        return await db.LoginHistory.AsNoTracking().Where(h => h.UserId == userId).OrderByDescending(h => h.At).Take(100)
            .Select(h => new LoginHistoryDto(h.At, h.Email, h.Succeeded, h.FailureReason, h.IpAddress, h.UserAgent)).ToListAsync(ct);
    }

    /// <summary>League admins manage everyone; franchise managers only users of their own franchise.</summary>
    private Guid? ScopeFranchise(Guid? requested)
    {
        if (me.Has(Perm.UsersAdmin)) return requested;
        me.Require(Perm.UsersManage);
        return me.RequireFranchise(requested);
    }

    private async Task<User> LoadManageableUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User");
        if (me.Has(Perm.UsersAdmin)) return user;
        var fid = ScopeFranchise(null);
        var isLeague = await db.UserRoles.AnyAsync(r => r.UserId == userId && r.FranchiseId == null, ct);
        if (isLeague || !await db.UserRoles.AnyAsync(r => r.UserId == userId && r.FranchiseId == fid, ct))
            throw new ForbiddenException("You can only manage users of your own franchise.");
        return user;
    }

    /// <summary>Stops privilege escalation: franchise managers can only grant roles whose permissions they hold.</summary>
    private async Task<List<Role>> ResolveAssignableRolesAsync(Guid? franchiseId, IReadOnlyList<string> codes, CancellationToken ct)
    {
        var roles = await db.Roles.Include(r => r.Permissions).ThenInclude(p => p.Permission)
            .Where(r => codes.Contains(r.Code)).ToListAsync(ct);
        if (roles.Count != codes.Distinct().Count()) throw new AppValidationException("roleCodes", "One or more roles do not exist.");

        if (me.Has(Perm.UsersAdmin))
        {
            // Platform roles hold every permission; only someone who already does may grant one.
            if (roles.Any(r => r.Scope == RoleScope.Platform) && !Perm.All.All(p => me.Has(p.Code)))
                throw new ForbiddenException("Only super admins can grant platform roles.");
        }
        else
        {
            var fid = ScopeFranchise(franchiseId);
            if (franchiseId != fid) throw new ForbiddenException("You can only assign roles in your own franchise.");
            if (roles.Any(r => r.Scope != RoleScope.Franchise)) throw new ForbiddenException("You can only assign franchise roles.");
            var missing = roles.SelectMany(r => r.Permissions.Select(p => p.Permission!.Code)).Where(p => !me.Has(p)).Distinct().ToList();
            if (missing.Count > 0) throw new ForbiddenException($"You cannot grant permissions you don't have ({string.Join(", ", missing.Take(3))}).");
        }
        if (roles.Any(r => r.Scope == RoleScope.Franchise) && franchiseId is null)
            throw new AppValidationException("franchiseId", "Choose the franchise for franchise roles.");
        if (franchiseId is { } f && !await db.Franchises.AnyAsync(x => x.Id == f, ct)) throw new NotFoundException("Franchise");
        return roles;
    }
}
