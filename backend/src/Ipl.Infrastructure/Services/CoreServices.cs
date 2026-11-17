using System.Text.Json;
using Ipl.Application;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Ipl.Infrastructure.Services;

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public class AuditService(IAppDbContext db, ICurrentUser user, IClock clock) : IAuditService
{
    public void Record(string action, string entityType, object? entityId, object? details = null, Guid? franchiseId = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            At = clock.UtcNow,
            UserId = user.IsAuthenticated ? user.UserId : null,
            UserEmail = user.IsAuthenticated ? user.Email : null,
            FranchiseId = franchiseId ?? user.FranchiseId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            Details = details is null ? null : JsonSerializer.Serialize(details),
            IpAddress = user.IpAddress,
        });
    }
}

/// <summary>
/// Cache over IDistributedCache (Redis when configured, otherwise in-process memory).
/// Prefix invalidation works by putting a version number into every key.
/// </summary>
public class AppCache(IDistributedCache cache) : IAppCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken ct = default)
    {
        var fullKey = await VersionedKey(key, ct);
        var cached = await cache.GetStringAsync(fullKey, ct);
        if (cached is not null)
        {
            var value = JsonSerializer.Deserialize<T>(cached, Json);
            if (value is not null) return value;
        }
        var created = await factory();
        await cache.SetStringAsync(fullKey, JsonSerializer.Serialize(created, Json),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
        return created;
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default) =>
        await cache.RemoveAsync(await VersionedKey(key, ct), ct);

    public async Task BumpAsync(string prefix, CancellationToken ct = default) =>
        await cache.SetStringAsync($"ver:{prefix}", Guid.NewGuid().ToString("N"), ct);

    private async Task<string> VersionedKey(string key, CancellationToken ct)
    {
        var prefix = key.Split(':')[0];
        var version = await cache.GetStringAsync($"ver:{prefix}", ct) ?? "0";
        return $"ipl:{prefix}:{version}:{key}";
    }
}

public class EmailQueue(IAppDbContext db, IClock clock) : IEmailQueue
{
    public void Enqueue(string to, string subject, string body) =>
        db.EmailMessages.Add(new EmailMessage { To = to, Subject = subject, Body = body, CreatedAt = clock.UtcNow });
}

public class NotificationService(IAppDbContext db, IRealtimePublisher realtime, IClock clock, IEmailQueue email) : INotificationService
{
    public async Task NotifyFranchiseAsync(Guid franchiseId, string type, string title, string body, string? link = null,
        string[]? permissions = null, string? dedupKey = null, CancellationToken ct = default)
    {
        var query = db.UserRoles.AsNoTracking().Where(r => r.FranchiseId == franchiseId && r.User!.IsActive);
        if (permissions is { Length: > 0 })
            query = query.Where(r => r.Role!.Permissions.Any(p => permissions.Contains(p.Permission!.Code)));
        var users = await query.Select(r => r.UserId).Distinct().ToListAsync(ct);
        await NotifyUsersAsync(users, type, title, body, link, franchiseId, dedupKey, ct);
    }

    public async Task NotifyLeagueAsync(string permission, string type, string title, string body, string? link = null, CancellationToken ct = default)
    {
        var users = await db.UserRoles.AsNoTracking()
            .Where(r => r.FranchiseId == null && r.User!.IsActive && r.Role!.Permissions.Any(p => p.Permission!.Code == permission))
            .Select(r => r.UserId).Distinct().ToListAsync(ct);
        await NotifyUsersAsync(users, type, title, body, link, null, null, ct);
    }

    public async Task NotifyUsersAsync(IEnumerable<Guid> userIds, string type, string title, string body, string? link = null,
        Guid? franchiseId = null, string? dedupKey = null, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var prefs = await db.NotificationPreferences.AsNoTracking()
            .Where(p => ids.Contains(p.UserId) && p.Type == type).ToDictionaryAsync(p => p.UserId, ct);
        var emails = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        HashSet<Guid> already = [];
        if (dedupKey is not null)
        {
            already = (await db.Notifications.IgnoreQueryFilters()
                .Where(n => ids.Contains(n.UserId) && n.DedupKey == dedupKey).Select(n => n.UserId).ToListAsync(ct)).ToHashSet();
        }
        var now = clock.UtcNow;
        foreach (var id in ids.Where(i => !already.Contains(i)))
        {
            var pref = prefs.GetValueOrDefault(id);
            if (pref is null || pref.InApp)
            {
                var n = new Notification
                {
                    UserId = id, FranchiseId = franchiseId, Type = type, Title = title, Body = body, Link = link,
                    CreatedAt = now, DedupKey = dedupKey,
                };
                db.Notifications.Add(n);
                await realtime.ToUser(id, "notification", new { n.Id, n.Type, n.Title, n.Body, n.Link, n.CreatedAt });
            }
            if (pref?.Email == true && emails.TryGetValue(id, out var to))
                email.Enqueue(to, title, $"{body}\n\n{link}");
        }
    }
}
