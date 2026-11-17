using System.Globalization;
using System.Text.Json;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Rules;

/// <summary>Typed, validated access to the configurable rules of one season.</summary>
public sealed class SeasonRules(int seasonId, IReadOnlyDictionary<string, string> values)
{
    public int SeasonId { get; } = seasonId;

    public bool Has(string key) => values.ContainsKey(key);

    private string Raw(string key) => values.TryGetValue(key, out var v)
        ? v
        : throw new DomainException("rules.missing", $"Season rule '{key}' is not configured for this season.");

    public int Int(string key) => int.Parse(Raw(key), CultureInfo.InvariantCulture);
    public decimal Decimal(string key) => decimal.Parse(Raw(key), CultureInfo.InvariantCulture);
    public bool Bool(string key) => bool.Parse(Raw(key));
    public DateOnly Date(string key) => DateOnly.Parse(Raw(key), CultureInfo.InvariantCulture);
    public T Json<T>(string key) => JsonSerializer.Deserialize<T>(Raw(key), JsonOptions.Default)
        ?? throw new DomainException("rules.invalid", $"Season rule '{key}' is empty.");

    public bool BoolOr(string key, bool fallback) => Has(key) ? Bool(key) : fallback;
    public int IntOr(string key, int fallback) => Has(key) ? Int(key) : fallback;
    public DateOnly? DateOrNull(string key) => Has(key) && values[key] != "" ? Date(key) : null;

    public IReadOnlyList<BidIncrementTier> BidIncrements() => Json<List<BidIncrementTier>>(RuleKeys.BidIncrements);
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web);
}

public interface IRulesService
{
    Task<Season> CurrentSeasonAsync(CancellationToken ct = default);
    Task<SeasonRules> ForSeasonAsync(int seasonId, CancellationToken ct = default);
    /// <summary>Checks a value is valid for the rule's type; throws AppValidationException if not.</summary>
    void Validate(string key, string value);
    Task InvalidateAsync(int seasonId);
}

public class RulesService(IAppDbContext db, IAppCache cache) : IRulesService
{
    public async Task<Season> CurrentSeasonAsync(CancellationToken ct = default) =>
        await db.Seasons.AsNoTracking().OrderByDescending(s => s.IsCurrent).ThenByDescending(s => s.Year).FirstOrDefaultAsync(ct)
        ?? throw new DomainException("season.none", "No season has been set up yet.");

    public async Task<SeasonRules> ForSeasonAsync(int seasonId, CancellationToken ct = default)
    {
        var dict = await cache.GetOrCreateAsync($"rules:{seasonId}", async () =>
            await db.SeasonRules.AsNoTracking().Where(r => r.SeasonId == seasonId).ToDictionaryAsync(r => r.Key, r => r.Value, ct),
            TimeSpan.FromMinutes(10), ct);
        return new SeasonRules(seasonId, dict);
    }

    public Task InvalidateAsync(int seasonId) => cache.RemoveAsync($"rules:{seasonId}");

    public void Validate(string key, string value)
    {
        var def = RuleKeys.Definitions.FirstOrDefault(d => d.Key == key);
        if (def.Key is null) throw new AppValidationException("key", $"Unknown rule '{key}'.");
        var ok = def.Type switch
        {
            RuleValueType.Integer => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0,
            RuleValueType.Decimal => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0,
            RuleValueType.Boolean => bool.TryParse(value, out _),
            RuleValueType.Date => value == "" || DateOnly.TryParse(value, CultureInfo.InvariantCulture, out _),
            RuleValueType.Json => IsValidJson(key, value),
            _ => true,
        };
        if (!ok) throw new AppValidationException("value", $"'{value}' is not a valid {def.Type} for {def.Description}.");
    }

    private static bool IsValidJson(string key, string value)
    {
        try
        {
            if (key == RuleKeys.BidIncrements)
            {
                var tiers = JsonSerializer.Deserialize<List<BidIncrementTier>>(value, JsonOptions.Default);
                return tiers is { Count: > 0 } && tiers.All(t => t.Increment > 0) && tiers[^1].UpTo is null;
            }
            if (key == RuleKeys.CappedRetentionCosts)
            {
                var costs = JsonSerializer.Deserialize<List<decimal>>(value, JsonOptions.Default);
                return costs is not null && costs.All(c => c >= 0);
            }
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
