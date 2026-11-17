using Ipl.Application;
using Ipl.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ipl.Api.Infrastructure;

/// <summary>
/// One hub for all live updates. Users join their own group and their franchise's group on connect; auction
/// rooms are joined explicitly and require the auctions.read permission. Clients never send data that changes state:
/// bids and other actions go through the REST API, which validates them.
/// </summary>
[Authorize]
public class LiveHub : Hub
{
    // The connection's own validated principal (IHttpContextAccessor is not reliable inside hub calls).
    private string? Claim(string type) => Context.User?.FindFirst(type)?.Value;

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)}");
        if (Claim(Claims.FranchiseId) is { } fid) await Groups.AddToGroupAsync(Context.ConnectionId, $"franchise:{fid}");
        await base.OnConnectedAsync();
    }

    public async Task JoinAuction(Guid auctionId)
    {
        if (Context.User?.HasClaim(Claims.Permission, Perm.AuctionsRead) != true) throw new HubException("You cannot watch auctions.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"auction:{auctionId}");
    }

    public Task LeaveAuction(Guid auctionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"auction:{auctionId}");
}

public class SignalRPublisher(IHubContext<LiveHub> hub) : IRealtimePublisher
{
    public Task ToUser(Guid userId, string eventName, object payload) => hub.Clients.Group($"user:{userId}").SendAsync(eventName, payload);
    public Task ToFranchise(Guid franchiseId, string eventName, object payload) => hub.Clients.Group($"franchise:{franchiseId}").SendAsync(eventName, payload);
    public Task ToAuction(Guid auctionId, string eventName, object payload) => hub.Clients.Group($"auction:{auctionId}").SendAsync(eventName, payload);
    public Task ToAll(string eventName, object payload) => hub.Clients.All.SendAsync(eventName, payload);
}
