using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Ipl.Application;
using Ipl.Application.Identity;
using Ipl.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ipl.Api.Infrastructure;

public class JwtOptions
{
    public string Issuer { get; set; } = "ipl-franchise-platform";
    public string Audience { get; set; } = "ipl-franchise-web";
    /// <summary>At least 32 random characters. Must come from configuration/secrets, never source control.</summary>
    public string SigningKey { get; set; } = "";
}

public static class Claims
{
    public const string FranchiseId = "fid";
    public const string LeagueWide = "lw";
    public const string SecurityStamp = "stamp";
    public const string Permission = "perm";
}

public class JwtTokenService(IOptions<JwtOptions> jwt, AuthOptions auth, IClock clock) : ITokenService
{
    public AccessToken CreateAccessToken(User user, Guid? franchiseId, IReadOnlyCollection<string> permissions, bool leagueWide)
    {
        var o = jwt.Value;
        var expires = clock.UtcNow.AddMinutes(auth.AccessTokenMinutes);
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(Claims.SecurityStamp, user.SecurityStamp),
            new(Claims.LeagueWide, leagueWide ? "1" : "0"),
        ];
        if (franchiseId is { } fid) claims.Add(new Claim(Claims.FranchiseId, fid.ToString()));
        claims.AddRange(permissions.Select(p => new Claim(Claims.Permission, p)));
        var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, clock.UtcNow, expires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)), SecurityAlgorithms.HmacSha256));
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

/// <summary>ICurrentUser from the validated JWT. Background jobs (no HttpContext) are unauthenticated and unscoped.</summary>
public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    private HashSet<string>? permissions;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public Guid UserId => Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    public string Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email) ?? Principal?.FindFirstValue(ClaimTypes.Email) ?? "";
    public string Name => Principal?.FindFirstValue(JwtRegisteredClaimNames.Name) ?? Principal?.FindFirstValue(ClaimTypes.Name) ?? "";
    public Guid? FranchiseId => Guid.TryParse(Principal?.FindFirstValue(Claims.FranchiseId), out var id) ? id : null;
    public bool IsLeagueWide => Principal?.FindFirstValue(Claims.LeagueWide) == "1";
    public IReadOnlySet<string> Permissions => permissions ??= Principal?.FindAll(Claims.Permission).Select(c => c.Value).ToHashSet() ?? [];
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public bool Has(string permission) => IsAuthenticated && Permissions.Contains(permission);
}

/// <summary>[HasPermission("trades.create")] → policy "perm:trades.create" (checked again inside the services).</summary>
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute($"perm:{permission}");

public class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith("perm:", StringComparison.Ordinal))
            return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(Claims.Permission, policyName[5..]).Build();
        return await base.GetPolicyAsync(policyName);
    }
}

public static class HttpContextExtensions
{
    public static string? ClientIp(this HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString();
    public static string? UserAgent(this HttpContext ctx) => ctx.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
    public static IPAddress? Ip(this HttpContext ctx) => ctx.Connection.RemoteIpAddress;
}
