using Ipl.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Ipl.Application;

/// <summary>The signed-in user and the franchise they are acting for (tenant).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    string Email { get; }
    string Name { get; }
    /// <summary>Active franchise, or null for platform/league users acting league-wide.</summary>
    Guid? FranchiseId { get; }
    IReadOnlySet<string> Permissions { get; }
    /// <summary>Platform/league staff: not limited to one franchise's data.</summary>
    bool IsLeagueWide { get; }
    string? IpAddress { get; }
    bool Has(string permission);
}

public static class CurrentUserExtensions
{
    public static void Require(this ICurrentUser user, string permission)
    {
        if (!user.Has(permission)) throw new ForbiddenException($"You need the '{permission}' permission.");
    }

    /// <summary>The franchise the user acts for; league-wide users must name one explicitly.</summary>
    public static Guid RequireFranchise(this ICurrentUser user, Guid? requested = null)
    {
        if (requested is { } id && (user.IsLeagueWide || user.FranchiseId == id)) return id;
        if (requested is not null) throw new ForbiddenException("You can only act for your own franchise.");
        return user.FranchiseId ?? throw new AppValidationException("franchise", "Select a franchise first.");
    }

    /// <summary>True if the user may see confidential data of this franchise.</summary>
    public static bool CanAccessFranchise(this ICurrentUser user, Guid franchiseId) =>
        user.IsLeagueWide || user.FranchiseId == franchiseId;

    public static void RequireFranchiseAccess(this ICurrentUser user, Guid franchiseId)
    {
        if (!user.CanAccessFranchise(franchiseId)) throw new ForbiddenException("This data belongs to another franchise.");
    }
}

public interface IClock
{
    DateTime UtcNow { get; }
    DateOnly Today => DateOnly.FromDateTime(UtcNow);
}

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<LoginHistory> LoginHistory { get; }
    DbSet<UserToken> UserTokens { get; }
    DbSet<EmailMessage> EmailMessages { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Season> Seasons { get; }
    DbSet<SeasonRule> SeasonRules { get; }
    DbSet<Franchise> Franchises { get; }
    DbSet<FranchiseSeason> FranchiseSeasons { get; }
    DbSet<FranchiseStaff> FranchiseStaff { get; }
    DbSet<FranchiseMember> FranchiseMembers { get; }
    DbSet<SystemSetting> SystemSettings { get; }
    DbSet<Country> Countries { get; }
    DbSet<PlayerRoleType> PlayerRoles { get; }
    DbSet<Player> Players { get; }
    DbSet<PlayerTeam> PlayerTeams { get; }
    DbSet<PlayerAvailability> PlayerAvailability { get; }
    DbSet<Competition> Competitions { get; }
    DbSet<Match> Matches { get; }
    DbSet<PlayerMatchStatistic> PlayerMatchStatistics { get; }
    DbSet<PlayerSeasonStatistic> PlayerSeasonStatistics { get; }
    DbSet<PlayerCareerStatistic> PlayerCareerStatistics { get; }
    DbSet<PersonSeasonStat> PersonSeasonStats { get; }
    DbSet<BatterBowlerMatchup> BatterBowlerMatchups { get; }
    DbSet<TradeOffer> TradeOffers { get; }
    DbSet<TradeOfferPlayer> TradeOfferPlayers { get; }
    DbSet<TradeNegotiation> TradeNegotiations { get; }
    DbSet<TradeApproval> TradeApprovals { get; }
    DbSet<TradeTransaction> TradeTransactions { get; }
    DbSet<FranchiseSquad> FranchiseSquads { get; }
    DbSet<PlayingXI> PlayingXIs { get; }
    DbSet<PlayingXIPlayer> PlayingXIPlayers { get; }
    DbSet<RetentionPlan> RetentionPlans { get; }
    DbSet<RetentionRecord> RetentionRecords { get; }
    DbSet<ReleaseRecord> ReleaseRecords { get; }
    DbSet<ReplacementRequest> ReplacementRequests { get; }
    DbSet<AuctionEvent> AuctionEvents { get; }
    DbSet<AuctionPlayer> AuctionPlayers { get; }
    DbSet<AuctionBid> AuctionBids { get; }
    DbSet<AuctionResult> AuctionResults { get; }
    DbSet<PlayerContract> PlayerContracts { get; }
    DbSet<ContractPayment> ContractPayments { get; }
    DbSet<FinanceTransaction> FinanceTransactions { get; }
    DbSet<BudgetAllocation> BudgetAllocations { get; }
    DbSet<ScoutingAssignment> ScoutingAssignments { get; }
    DbSet<PlayerScoutingReport> PlayerScoutingReports { get; }
    DbSet<PlayerValuation> PlayerValuations { get; }
    DbSet<PlayerComparison> PlayerComparisons { get; }
    DbSet<PlayerMedicalRecord> PlayerMedicalRecords { get; }
    DbSet<MessageThread> MessageThreads { get; }
    DbSet<Message> Messages { get; }
    DbSet<MessageThreadRead> MessageThreadReads { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<Document> Documents { get; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Writes audit log entries for sensitive operations (saved with the next SaveChanges).</summary>
public interface IAuditService
{
    void Record(string action, string entityType, object? entityId, object? details = null, Guid? franchiseId = null);
}

/// <summary>Real-time pushes (SignalR). Implemented in the API layer.</summary>
public interface IRealtimePublisher
{
    Task ToUser(Guid userId, string eventName, object payload);
    Task ToFranchise(Guid franchiseId, string eventName, object payload);
    Task ToAuction(Guid auctionId, string eventName, object payload);
    Task ToAll(string eventName, object payload);
}

public interface INotificationService
{
    /// <summary>Notifies every active user of a franchise who holds one of the permissions (or all if none given).</summary>
    Task NotifyFranchiseAsync(Guid franchiseId, string type, string title, string body, string? link = null,
        string[]? permissions = null, string? dedupKey = null, CancellationToken ct = default);

    Task NotifyUsersAsync(IEnumerable<Guid> userIds, string type, string title, string body, string? link = null,
        Guid? franchiseId = null, string? dedupKey = null, CancellationToken ct = default);

    /// <summary>Notifies league-wide users holding a permission (e.g. league approvals).</summary>
    Task NotifyLeagueAsync(string permission, string type, string title, string body, string? link = null, CancellationToken ct = default);
}

public interface IEmailQueue
{
    void Enqueue(string to, string subject, string body);
}

/// <summary>Cache for expensive, mostly static data (statistics, aggregates). Redis or in-memory.</summary>
public interface IAppCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    /// <summary>Invalidates every key that starts with the prefix (tracked via a version stamp).</summary>
    Task BumpAsync(string prefix, CancellationToken ct = default);
}

// ---------- Errors (mapped to HTTP status codes by the API) ----------

public class NotFoundException(string what) : Exception($"{what} was not found.");

public class ForbiddenException(string message) : Exception(message);

public class AppValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public class ConflictException(string message) : Exception(message);

// ---------- Shared query/paging types ----------

public record PageRequest(int Page = 1, int PageSize = 25, string? Sort = null, bool Desc = false)
{
    public int SafePage => Math.Max(1, Page);
    public int SafeSize => Math.Clamp(PageSize, 1, 200);
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageRequest page, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page.SafePage - 1) * page.SafeSize).Take(page.SafeSize).ToListAsync(ct);
        return new PagedResult<T>(items, page.SafePage, page.SafeSize, total);
    }
}
