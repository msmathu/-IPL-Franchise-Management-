using System.Security.Cryptography;
using System.Text;
using Ipl.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Identity;

public record AccessToken(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user, Guid? franchiseId, IReadOnlyCollection<string> permissions, bool leagueWide);
}

public record UserFranchiseDto(Guid Id, string Name, string ShortName, string Code, string PrimaryColor, IReadOnlyList<string> Roles);

public record MeDto(
    Guid Id, string Email, string FullName, Guid? ActiveFranchiseId, bool IsLeagueWide,
    IReadOnlyList<string> LeagueRoles, IReadOnlyList<UserFranchiseDto> Franchises, IReadOnlyList<string> Permissions);

public record AuthResult(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt, MeDto User);

public record SessionDto(Guid Id, DateTime CreatedAt, DateTime? LastUsedAt, DateTime ExpiresAt, string? IpAddress, string? UserAgent, bool IsCurrent);

public record LoginHistoryDto(DateTime At, string Email, bool Succeeded, string? FailureReason, string? IpAddress, string? UserAgent);

public class AuthOptions
{
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    /// <summary>Base URL of the web app, used in e-mailed links.</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:4200";
}

public static class Secrets
{
    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(48));

    public static string Hash(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();

    /// <summary>At least 10 characters with upper case, lower case and a digit.</summary>
    public static void ValidatePassword(string password)
    {
        if (password.Length < 10 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            throw new AppValidationException("password", "Use at least 10 characters with upper and lower case letters and a number.");
    }
}

public class AuthService(
    IAppDbContext db, ITokenService tokens, IClock clock, IEmailQueue email, IAuditService audit,
    IPasswordHasher<User> hasher, AuthOptions options)
{
    private const string GenericLoginError = "Incorrect e-mail or password.";

    public async Task<AuthResult> LoginAsync(string emailAddress, string password, string? ip, string? userAgent, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var normalized = Secrets.Normalize(emailAddress);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);

        async Task<Exception> Fail(string reason, string message)
        {
            db.LoginHistory.Add(new LoginHistory { UserId = user?.Id, Email = emailAddress, Succeeded = false, FailureReason = reason, IpAddress = ip, UserAgent = userAgent, At = now });
            await db.SaveChangesAsync(ct);
            return new AuthFailedException(message);
        }

        if (user is null || user.PasswordHash is null) throw await Fail("unknown-user", GenericLoginError);
        if (!user.IsActive) throw await Fail("deactivated", "This account has been deactivated. Contact your administrator.");
        if (user.LockoutEndsAt > now)
            throw await Fail("locked", $"Too many failed attempts. Try again after {user.LockoutEndsAt:HH:mm} UTC or reset your password.");

        var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verify == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= options.MaxFailedLogins)
            {
                user.LockoutEndsAt = now.AddMinutes(options.LockoutMinutes);
                user.FailedLoginCount = 0;
            }
            throw await Fail("bad-password", GenericLoginError);
        }
        if (verify == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = hasher.HashPassword(user, password);

        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        user.LastLoginAt = now;
        db.LoginHistory.Add(new LoginHistory { UserId = user.Id, Email = user.Email, Succeeded = true, IpAddress = ip, UserAgent = userAgent, At = now });
        var franchiseId = await DefaultFranchiseAsync(user, ct);
        return await IssueAsync(user, franchiseId, Guid.NewGuid(), ip, userAgent, ct);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = Secrets.Hash(refreshToken);
        var token = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new AuthFailedException("Your session has expired. Please sign in again.");

        if (token.RevokedAt is not null)
        {
            // A rotated token was used again: it may have been stolen. End every session in this family.
            if (token.ReplacedById is not null)
            {
                await db.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, "reuse-detected"), ct);
                audit.Record("auth.refresh_reuse", "User", token.UserId, new { token.FamilyId });
                await db.SaveChangesAsync(ct);
            }
            throw new AuthFailedException("Your session has expired. Please sign in again.");
        }
        if (token.ExpiresAt <= now || token.User is not { IsActive: true })
            throw new AuthFailedException("Your session has expired. Please sign in again.");

        var franchiseId = token.FranchiseId;
        if (franchiseId is not null && !await HasFranchiseAsync(token.UserId, franchiseId.Value, ct)) franchiseId = await DefaultFranchiseAsync(token.User, ct);
        var result = await IssueAsync(token.User, franchiseId, token.FamilyId, ip, userAgent, ct, rotated: token);
        return result;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Secrets.Hash(refreshToken);
        await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow).SetProperty(t => t.RevokedReason, "logout"), ct);
    }

    public async Task<AuthResult> SwitchFranchiseAsync(Guid userId, Guid franchiseId, string refreshToken, string? ip, string? userAgent, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var isLeague = await db.UserRoles.AnyAsync(r => r.UserId == userId && r.FranchiseId == null, ct);
        if (!isLeague && !await HasFranchiseAsync(userId, franchiseId, ct)) throw new ForbiddenException("You are not a member of that franchise.");
        var hash = Secrets.Hash(refreshToken);
        var old = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId && t.RevokedAt == null, ct)
            ?? throw new AuthFailedException("Your session has expired. Please sign in again.");
        user.DefaultFranchiseId = franchiseId;
        return await IssueAsync(user, franchiseId, old.FamilyId, ip, userAgent, ct, rotated: old);
    }

    public async Task ForgotPasswordAsync(string emailAddress, CancellationToken ct)
    {
        var normalized = Secrets.Normalize(emailAddress);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized && u.IsActive, ct);
        if (user is null) return; // Same response either way: don't reveal which e-mails exist.
        var token = Secrets.NewToken();
        db.UserTokens.Add(new UserToken
        {
            UserId = user.Id, Purpose = UserTokenPurpose.PasswordReset, TokenHash = Secrets.Hash(token),
            CreatedAt = clock.UtcNow, ExpiresAt = clock.UtcNow.AddHours(1),
        });
        email.Enqueue(user.Email, "Reset your password",
            $"Hello {user.FullName},\n\nUse this link within 1 hour to choose a new password:\n{options.AppBaseUrl}/reset-password?token={token}\n\nIf you did not ask for this, ignore this e-mail.");
        audit.Record("auth.password_reset_requested", "User", user.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct)
    {
        Secrets.ValidatePassword(newPassword);
        var userToken = await UseTokenAsync(token, UserTokenPurpose.PasswordReset, ct);
        var user = await db.Users.FirstAsync(u => u.Id == userToken.UserId, ct);
        await SetPasswordAsync(user, newPassword, "password-reset", ct);
        audit.Record("auth.password_reset", "User", user.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task AcceptInvitationAsync(string token, string password, string? fullName, CancellationToken ct)
    {
        Secrets.ValidatePassword(password);
        var userToken = await UseTokenAsync(token, UserTokenPurpose.Invitation, ct);
        var user = await db.Users.FirstAsync(u => u.Id == userToken.UserId, ct);
        if (!string.IsNullOrWhiteSpace(fullName)) user.FullName = fullName.Trim();
        user.IsActivated = true;
        await SetPasswordAsync(user, password, "invitation-accepted", ct);
        audit.Record("auth.invitation_accepted", "User", user.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        Secrets.ValidatePassword(newPassword);
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        if (user.PasswordHash is null || hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            throw new AppValidationException("currentPassword", "Your current password is not correct.");
        await SetPasswordAsync(user, newPassword, "password-changed", ct);
        audit.Record("auth.password_changed", "User", user.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SessionDto>> SessionsAsync(Guid userId, string? currentRefreshToken, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var currentHash = currentRefreshToken is null ? null : Secrets.Hash(currentRefreshToken);
        return await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new SessionDto(t.Id, t.CreatedAt, t.LastUsedAt, t.ExpiresAt, t.CreatedByIp, t.UserAgent, t.TokenHash == currentHash))
            .ToListAsync(ct);
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var count = await db.RefreshTokens.Where(t => t.Id == sessionId && t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow).SetProperty(t => t.RevokedReason, "revoked-by-user"), ct);
        if (count == 0) throw new NotFoundException("Session");
    }

    public async Task<IReadOnlyList<LoginHistoryDto>> LoginHistoryAsync(Guid userId, CancellationToken ct) =>
        await db.LoginHistory.AsNoTracking().Where(h => h.UserId == userId).OrderByDescending(h => h.At).Take(50)
            .Select(h => new LoginHistoryDto(h.At, h.Email, h.Succeeded, h.FailureReason, h.IpAddress, h.UserAgent)).ToListAsync(ct);

    public async Task<MeDto> MeAsync(Guid userId, Guid? franchiseId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        return (await BuildAccessAsync(user, franchiseId, ct)).Me;
    }

    // ------------------------------------------------------------------

    private async Task<AuthResult> IssueAsync(User user, Guid? franchiseId, Guid familyId, string? ip, string? userAgent, CancellationToken ct, RefreshToken? rotated = null)
    {
        var now = clock.UtcNow;
        var access = await BuildAccessAsync(user, franchiseId, ct);
        var refresh = Secrets.NewToken();
        var entity = new RefreshToken
        {
            UserId = user.Id, TokenHash = Secrets.Hash(refresh), FamilyId = familyId, FranchiseId = access.Me.ActiveFranchiseId,
            CreatedAt = now, ExpiresAt = now.AddDays(options.RefreshTokenDays), CreatedByIp = ip,
            UserAgent = userAgent?.Length > 400 ? userAgent[..400] : userAgent, LastUsedAt = now,
        };
        db.RefreshTokens.Add(entity);
        if (rotated is not null)
        {
            rotated.RevokedAt = now;
            rotated.RevokedReason = "rotated";
            rotated.ReplacedById = entity.Id;
        }
        await db.SaveChangesAsync(ct);
        var jwt = tokens.CreateAccessToken(user, access.Me.ActiveFranchiseId, access.Me.Permissions, access.Me.IsLeagueWide);
        return new AuthResult(jwt.Token, jwt.ExpiresAt, refresh, entity.ExpiresAt, access.Me);
    }

    private sealed record Access(MeDto Me);

    private async Task<Access> BuildAccessAsync(User user, Guid? franchiseId, CancellationToken ct)
    {
        var roles = await db.UserRoles.AsNoTracking().Where(r => r.UserId == user.Id)
            .Select(r => new
            {
                r.FranchiseId, r.Role!.Name, r.Role.Code,
                Permissions = r.Role.Permissions.Select(p => p.Permission!.Code).ToList(),
            })
            .ToListAsync(ct);
        var leagueRoles = roles.Where(r => r.FranchiseId == null).ToList();
        var isLeagueWide = leagueRoles.Count > 0;
        var franchiseIds = roles.Where(r => r.FranchiseId != null).Select(r => r.FranchiseId!.Value).Distinct().ToList();
        var franchises = await db.Franchises.AsNoTracking().Where(f => franchiseIds.Contains(f.Id) && f.IsActive)
            .OrderBy(f => f.Name).ToListAsync(ct);

        Guid? active = franchiseId is { } fid && (franchises.Any(f => f.Id == fid) || isLeagueWide && await db.Franchises.AnyAsync(f => f.Id == fid, ct))
            ? fid
            : isLeagueWide ? null : franchises.FirstOrDefault()?.Id;

        var permissions = leagueRoles.SelectMany(r => r.Permissions)
            .Concat(roles.Where(r => active != null && r.FranchiseId == active).SelectMany(r => r.Permissions))
            .Distinct().OrderBy(p => p).ToList();

        var franchiseDtos = franchises.Select(f => new UserFranchiseDto(f.Id, f.Name, f.ShortName, f.Code, f.PrimaryColor,
            roles.Where(r => r.FranchiseId == f.Id).Select(r => r.Name).ToList())).ToList();
        return new Access(new MeDto(user.Id, user.Email, user.FullName, active, isLeagueWide,
            leagueRoles.Select(r => r.Name).ToList(), franchiseDtos, permissions));
    }

    private async Task<Guid?> DefaultFranchiseAsync(User user, CancellationToken ct)
    {
        if (user.DefaultFranchiseId is { } d && await HasFranchiseAsync(user.Id, d, ct)) return d;
        return await db.UserRoles.Where(r => r.UserId == user.Id && r.FranchiseId != null).Select(r => r.FranchiseId).FirstOrDefaultAsync(ct);
    }

    private Task<bool> HasFranchiseAsync(Guid userId, Guid franchiseId, CancellationToken ct) =>
        db.UserRoles.AnyAsync(r => r.UserId == userId && r.FranchiseId == franchiseId, ct);

    private async Task<UserToken> UseTokenAsync(string token, UserTokenPurpose purpose, CancellationToken ct)
    {
        var hash = Secrets.Hash(token);
        var userToken = await db.UserTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);
        if (userToken is null || userToken.UsedAt is not null || userToken.ExpiresAt <= clock.UtcNow)
            throw new AppValidationException("token", "This link is invalid or has expired. Ask for a new one.");
        userToken.UsedAt = clock.UtcNow;
        return userToken;
    }

    private async Task SetPasswordAsync(User user, string password, string reason, CancellationToken ct)
    {
        user.PasswordHash = hasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow).SetProperty(t => t.RevokedReason, reason), ct);
    }
}

public class AuthFailedException(string message) : Exception(message);
