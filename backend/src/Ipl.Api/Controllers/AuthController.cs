using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Ipl.Api.Infrastructure;
using Ipl.Application;
using Ipl.Application.Identity;
using Ipl.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ipl.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public abstract class ApiControllerBase : ControllerBase;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public record ForgotPasswordRequest([Required, EmailAddress] string Email);
public record ResetPasswordRequest([Required] string Token, [Required] string NewPassword);
public record AcceptInvitationRequest([Required] string Token, [Required] string Password, string? FullName);
public record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword);
public record SwitchFranchiseRequest(Guid FranchiseId);
public record LoginResponse(string AccessToken, DateTime AccessTokenExpiresAt, MeDto User);

/// <summary>
/// Access tokens are returned in the body and kept in memory by the web app; the refresh token is an HttpOnly,
/// Secure, SameSite=Strict cookie scoped to /api/v1/auth, so scripts can never read it.
/// </summary>
[Route("api/v{version:apiVersion}/auth")]
public class AuthController(AuthService auth, ICurrentUser me) : ApiControllerBase
{
    private const string RefreshCookie = "ipl_refresh";

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<LoginResponse> Login(LoginRequest req, CancellationToken ct) =>
        Issue(await auth.LoginAsync(req.Email, req.Password, HttpContext.ClientIp(), HttpContext.UserAgent(), ct));

    [AllowAnonymous, HttpPost("refresh"), EnableRateLimiting("refresh")]
    public async Task<LoginResponse> Refresh(CancellationToken ct)
    {
        var token = Request.Cookies[RefreshCookie] ?? throw new AuthFailedException("Please sign in.");
        return Issue(await auth.RefreshAsync(token, HttpContext.ClientIp(), HttpContext.UserAgent(), ct));
    }

    [AllowAnonymous, HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies[RefreshCookie] is { } token) await auth.LogoutAsync(token, ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UnixEpoch));
        return NoContent();
    }

    [AllowAnonymous, HttpPost("forgot-password"), EnableRateLimiting("auth")]
    public async Task<object> ForgotPassword(ForgotPasswordRequest req, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(req.Email, ct);
        return new { message = "If that e-mail has an account, a reset link has been sent." };
    }

    [AllowAnonymous, HttpPost("reset-password"), EnableRateLimiting("auth")]
    public async Task<object> ResetPassword(ResetPasswordRequest req, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(req.Token, req.NewPassword, ct);
        return new { message = "Password changed. Please sign in." };
    }

    [AllowAnonymous, HttpPost("accept-invitation"), EnableRateLimiting("auth")]
    public async Task<object> AcceptInvitation(AcceptInvitationRequest req, CancellationToken ct)
    {
        await auth.AcceptInvitationAsync(req.Token, req.Password, req.FullName, ct);
        return new { message = "Your account is ready. Please sign in." };
    }

    [HttpGet("me")]
    public Task<MeDto> Me(CancellationToken ct) => auth.MeAsync(me.UserId, me.FranchiseId, ct);

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(me.UserId, req.CurrentPassword, req.NewPassword, ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UnixEpoch));
        return NoContent();
    }

    [HttpPost("switch-franchise")]
    public async Task<LoginResponse> SwitchFranchise(SwitchFranchiseRequest req, CancellationToken ct)
    {
        var token = Request.Cookies[RefreshCookie] ?? throw new AuthFailedException("Please sign in again.");
        return Issue(await auth.SwitchFranchiseAsync(me.UserId, req.FranchiseId, token, HttpContext.ClientIp(), HttpContext.UserAgent(), ct));
    }

    [HttpGet("sessions")]
    public Task<IReadOnlyList<SessionDto>> Sessions(CancellationToken ct) => auth.SessionsAsync(me.UserId, Request.Cookies[RefreshCookie], ct);

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        await auth.RevokeSessionAsync(me.UserId, id, ct);
        return NoContent();
    }

    [HttpGet("login-history")]
    public Task<IReadOnlyList<LoginHistoryDto>> LoginHistory(CancellationToken ct) => auth.LoginHistoryAsync(me.UserId, ct);

    private LoginResponse Issue(AuthResult r)
    {
        Response.Cookies.Append(RefreshCookie, r.RefreshToken, CookieOptions(r.RefreshTokenExpiresAt));
        return new LoginResponse(r.AccessToken, r.AccessTokenExpiresAt, r.User);
    }

    private CookieOptions CookieOptions(DateTime expires) => new()
    {
        HttpOnly = true, Secure = Request.IsHttps || !HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
        SameSite = SameSiteMode.Strict, Path = "/api/v1/auth", Expires = expires,
    };
}

[Route("api/v{version:apiVersion}/users")]
public class UsersController(UserAdminService users) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserListItemDto>> List([FromQuery] Guid? franchiseId, [FromQuery] string? search, [FromQuery] PageRequest page, CancellationToken ct) =>
        users.ListAsync(franchiseId, search, page, ct);

    [HttpGet("roles")]
    public Task<IReadOnlyList<RoleDto>> Roles(CancellationToken ct) => users.RolesAsync(ct);

    [HttpGet("permissions")]
    public IReadOnlyList<PermissionDto> Permissions() => users.Permissions();

    [HttpPost("invite"), EnableRateLimiting("auth")]
    public Task<InviteResult> Invite(InviteUserRequest req, CancellationToken ct) => users.InviteAsync(req, ct);

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) { await users.SetActiveAsync(id, true, ct); return NoContent(); }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) { await users.SetActiveAsync(id, false, ct); return NoContent(); }

    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> UpdateRoles(Guid id, UpdateUserRolesRequest req, CancellationToken ct) { await users.UpdateRolesAsync(id, req, ct); return NoContent(); }

    [HttpGet("{id:guid}/login-history")]
    public Task<IReadOnlyList<LoginHistoryDto>> LoginHistory(Guid id, CancellationToken ct) => users.LoginHistoryAsync(id, ct);
}
