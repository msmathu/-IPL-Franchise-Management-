using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Infrastructure.Persistence;

/// <summary>Keeps permissions, roles and lookups in sync with the code on every start (safe to run repeatedly).</summary>
public static class CatalogSeeder
{
    public static async Task SyncAsync(AppDbContext db, CancellationToken ct)
    {
        var perms = await db.Permissions.ToDictionaryAsync(p => p.Code, ct);
        foreach (var (code, module, description) in Perm.All)
        {
            if (!perms.TryGetValue(code, out var p)) db.Permissions.Add(p = new Permission { Code = code });
            p.Module = module;
            p.Description = description;
            perms[code] = p;
        }
        await db.SaveChangesAsync(ct);

        var roles = await db.Roles.Include(r => r.Permissions).ToDictionaryAsync(r => r.Code, ct);
        var order = 0;
        foreach (var (code, name, scope, description, permissions) in RoleCatalog.All)
        {
            if (!roles.TryGetValue(code, out var role)) db.Roles.Add(role = new Role { Code = code });
            role.Name = name;
            role.Scope = scope;
            role.Description = description;
            role.SortOrder = ++order;
            var wanted = permissions.Select(c => perms[c].Id).ToHashSet();
            role.Permissions.RemoveAll(rp => !wanted.Contains(rp.PermissionId));
            foreach (var pid in wanted.Where(pid => role.Permissions.All(rp => rp.PermissionId != pid)))
                role.Permissions.Add(new RolePermission { PermissionId = pid });
        }
        await db.SaveChangesAsync(ct);

        if (!await db.PlayerRoles.AnyAsync(ct))
        {
            db.PlayerRoles.AddRange(
                new PlayerRoleType { Code = "batter", Name = "Batter", Group = "Batting", SortOrder = 1 },
                new PlayerRoleType { Code = "wk_batter", Name = "Wicketkeeper-batter", Group = "Keeping", SortOrder = 2 },
                new PlayerRoleType { Code = "allrounder", Name = "All-rounder", Group = "AllRound", SortOrder = 3 },
                new PlayerRoleType { Code = "bowler", Name = "Bowler", Group = "Bowling", SortOrder = 4 });
        }
        if (!await db.Countries.AnyAsync(ct))
        {
            db.Countries.AddRange(
                new Country { Code = "IND", Name = "India", IsHomeCountry = true },
                new Country { Code = "AUS", Name = "Australia" }, new Country { Code = "ENG", Name = "England" },
                new Country { Code = "RSA", Name = "South Africa" }, new Country { Code = "NZL", Name = "New Zealand" },
                new Country { Code = "WIN", Name = "West Indies" }, new Country { Code = "AFG", Name = "Afghanistan" },
                new Country { Code = "SRI", Name = "Sri Lanka" }, new Country { Code = "BAN", Name = "Bangladesh" },
                new Country { Code = "IRE", Name = "Ireland" }, new Country { Code = "ZIM", Name = "Zimbabwe" },
                new Country { Code = "NED", Name = "Netherlands" });
        }
        if (!await db.SystemSettings.AnyAsync(s => s.Key == "rating.weights", ct))
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = "rating.weights", Description = "Rating model weights",
                Value = System.Text.Json.JsonSerializer.Serialize(Ipl.Application.Analytics.RatingWeights.Default, Ipl.Application.Rules.JsonOptions.Default),
                UpdatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
