using System.Text.Json;
using Ipl.Application.Players;
using Ipl.Application.Rules;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ipl.Application.Analytics;

/// <summary>Configurable weights of the rating model (SystemSettings key "rating.weights").</summary>
public record RatingWeights(decimal RecentForm, decimal Career, decimal Age, decimal Consistency, decimal CompetitionLevel, decimal Availability, decimal Potential)
{
    public static readonly RatingWeights Default = new(0.25m, 0.25m, 0.10m, 0.15m, 0.10m, 0.05m, 0.10m);
    public decimal Total => RecentForm + Career + Age + Consistency + CompetitionLevel + Availability + Potential;
}

public record RatingFactorDto(string Factor, decimal Score, decimal Weight, decimal Contribution, string Explanation);

public record PlayerRatingDto(
    Guid PlayerId, string PlayerName, string Role, int? Age, bool IsOverseas, decimal Rating, string Confidence, int MatchesUsed,
    IReadOnlyList<RatingFactorDto> Factors, string Method);

public record ValuationFactorDto(string Factor, string Effect, string Explanation);

public record ValuationDto(
    Guid PlayerId, string PlayerName, decimal Low, decimal Mid, decimal High, int DemandIndex, string DemandLabel, decimal? LastKnownPrice,
    decimal Rating, IReadOnlyList<ValuationFactorDto> Factors, string ModelVersion, DateTime GeneratedAt, string Disclaimer);

public record TeamStrengthDto(decimal Strength, IReadOnlyList<Guid> BestXI, int OverseasInXI, IReadOnlyList<string> Notes);

public class RatingService(IAppDbContext db, IClock clock, IMemoryCache memory)
{
    // Ratings are cached briefly in memory; bumping the version (after statistics or weights change) drops them all.
    private static int version;
    public static void Invalidate() => Interlocked.Increment(ref version);

    public const string ModelVersion = "rating-v1";

    public async Task<RatingWeights> WeightsAsync(CancellationToken ct)
    {
        var json = await db.SystemSettings.Where(s => s.Key == "rating.weights").Select(s => s.Value).FirstOrDefaultAsync(ct);
        return json is null ? RatingWeights.Default : JsonSerializer.Deserialize<RatingWeights>(json, JsonOptions.Default) ?? RatingWeights.Default;
    }

    public async Task<PlayerRatingDto> RateAsync(Guid playerId, CancellationToken ct) =>
        (await RateManyAsync([playerId], ct)).GetValueOrDefault(playerId) ?? throw new NotFoundException("Player");

    /// <summary>Rates many players with one query per data set. Only public availability is used (never medical details).</summary>
    public async Task<Dictionary<Guid, PlayerRatingDto>> RateManyAsync(IReadOnlyCollection<Guid> playerIds, CancellationToken ct)
    {
        var v = Volatile.Read(ref version);
        var result = new Dictionary<Guid, PlayerRatingDto>();
        var missing = new List<Guid>();
        foreach (var id in playerIds.Distinct())
        {
            if (memory.TryGetValue(("rating", v, id), out PlayerRatingDto? cached) && cached is not null) result[id] = cached;
            else missing.Add(id);
        }
        if (missing.Count == 0) return result;
        foreach (var (id, rating) in await ComputeAsync(missing, ct))
        {
            memory.Set(("rating", v, id), rating, TimeSpan.FromMinutes(10));
            result[id] = rating;
        }
        return result;
    }

    private async Task<Dictionary<Guid, PlayerRatingDto>> ComputeAsync(IReadOnlyCollection<Guid> playerIds, CancellationToken ct)
    {
        if (playerIds.Count == 0) return [];
        var weights = await WeightsAsync(ct);
        var today = clock.Today;
        var players = await db.Players.AsNoTracking().Where(p => playerIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.DateOfBirth, p.IsOverseas, Role = p.PrimaryRole!.Code, RoleName = p.PrimaryRole.Name })
            .ToListAsync(ct);
        var matches = await db.PlayerMatchStatistics.AsNoTracking()
            .Where(s => playerIds.Contains(s.PlayerId) && (s.Match!.Competition!.Format == MatchFormat.T20 || s.Match.Competition.Format == MatchFormat.T20I))
            .Select(s => new MatchLine(s.PlayerId, s.Match!.Date, s.Match.Competition!.Level, s.Batted, s.Runs, s.BallsFaced, s.Dismissed, s.Bowled, s.BallsBowled, s.RunsConceded, s.Wickets))
            .ToListAsync(ct);
        // Players with only imported season totals (no match rows) are rated from those totals.
        var totals = await db.PlayerCareerStatistics.AsNoTracking()
            .Where(c => playerIds.Contains(c.PlayerId) && (c.Competition!.Format == MatchFormat.T20 || c.Competition.Format == MatchFormat.T20I))
            .Select(c => new { c.PlayerId, c.Matches, c.Innings, c.NotOuts, c.Runs, c.BallsFaced, c.BallsBowled, c.RunsConceded, c.Wickets, c.Competition!.Level })
            .ToListAsync(ct);
        var avail = await db.PlayerAvailability.AsNoTracking().Where(a => playerIds.Contains(a.PlayerId)).ToDictionaryAsync(a => a.PlayerId, a => a.Status, ct);
        var byPlayer = matches.GroupBy(m => m.PlayerId).ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Date).ToList());

        var result = new Dictionary<Guid, PlayerRatingDto>();
        foreach (var p in players)
        {
            var lines = byPlayer.GetValueOrDefault(p.Id) ?? [];
            var age = PlayerService.AgeOn(p.DateOfBirth, today);
            var role = p.Role;
            List<RatingFactorDto> factors = [];

            decimal careerScore;
            string careerNote;
            if (lines.Count > 0)
            {
                careerScore = RoleIndex(role, lines);
                careerNote = $"{lines.Count} T20 matches: {Describe(role, lines)}";
            }
            else if (totals.Where(t => t.PlayerId == p.Id).ToList() is { Count: > 0 } t)
            {
                careerScore = RoleIndexFromTotals(role, t.Sum(x => x.Runs), t.Sum(x => x.BallsFaced), t.Sum(x => x.Innings - x.NotOuts),
                    t.Sum(x => x.BallsBowled), t.Sum(x => x.RunsConceded), t.Sum(x => x.Wickets));
                careerNote = $"Imported season totals ({t.Sum(x => x.Matches)} matches)";
            }
            else
            {
                careerScore = 40;
                careerNote = "No T20 data: neutral score";
            }
            var recent = lines.Take(10).ToList();
            var formScore = recent.Count >= 3 ? RoleIndex(role, recent) : careerScore;
            var last20 = lines.Take(20).ToList();
            var consistency = last20.Count >= 5
                ? 100m * last20.Count(l => l.Runs >= 25 || l.Wickets >= 2 || (l.Bowled && l.BallsBowled >= 18 && l.RunsConceded * 6m / l.BallsBowled <= 7.5m)) / last20.Count
                : 50m;
            // Unknown age (not published by the source) gets a neutral score instead of a guess.
            var ageScore = age switch { null => 70, <= 20 => 75, <= 23 => 88, <= 31 => 100, _ => Math.Max(30, 100 - (age.Value - 31) * 9) };
            var maxLevel = lines.Count > 0 ? lines.Max(l => l.Level) : totals.Where(t => t.PlayerId == p.Id).Select(t => t.Level).DefaultIfEmpty(1).Max();
            var levelScore = Math.Clamp(maxLevel * 20m, 20, 100);
            var availStatus = avail.GetValueOrDefault(p.Id, AvailabilityStatus.Available);
            var availabilityScore = availStatus switch
            {
                AvailabilityStatus.Available => 100m, AvailabilityStatus.Doubtful => 60m, AvailabilityStatus.NationalDuty => 50m,
                AvailabilityStatus.Personal => 50m, AvailabilityStatus.Injured => 20m, _ => 10m,
            };
            // Potential: young players whose recent form is ahead of their career numbers.
            var potential = age is null ? 50m : age <= 25 ? Math.Clamp(60 + (formScore - careerScore) * 1.5m + (25 - age.Value) * 3, 0, 100) : Math.Max(20, 60 - (age.Value - 25) * 5);

            void Add(string name, decimal score, decimal weight, string note) =>
                factors.Add(new RatingFactorDto(name, Math.Round(score, 1), weight, Math.Round(score * weight / weights.Total, 2), note));

            Add("Recent form", formScore, weights.RecentForm, recent.Count >= 3 ? $"Last {recent.Count} T20 matches: {Describe(role, recent)}" : "Fewer than 3 recent matches: career score used");
            Add("Career performance", careerScore, weights.Career, careerNote);
            Add("Age profile", ageScore, weights.Age, age is null ? "Date of birth not recorded: neutral score" : $"Age {age} (peak band 24–31)");
            Add("Consistency", consistency, weights.Consistency, last20.Count >= 5 ? $"{consistency:0}% of the last {last20.Count} matches had a meaningful contribution" : "Not enough matches: neutral");
            Add("Competition level", levelScore, weights.CompetitionLevel, $"Highest level played: {maxLevel} of 5");
            Add("Availability", availabilityScore, weights.Availability, $"Public status: {availStatus}");
            Add("Development potential", potential, weights.Potential, age is null ? "Age unknown: neutral" : age <= 25 ? "Young player: recent form vs career trend" : "Established player: limited upside");

            var rating = Math.Round(factors.Sum(f => f.Contribution), 1);
            var hasData = lines.Count > 0 || totals.Any(t => t.PlayerId == p.Id);
            var confidence = !hasData ? "None" : lines.Count >= 30 ? "High" : lines.Count >= 10 ? "Medium" : "Low";
            result[p.Id] = new PlayerRatingDto(p.Id, p.FullName, p.RoleName, age, p.IsOverseas, rating, confidence, lines.Count, factors,
                "Weighted sum of the factors below. Weights are configurable by administrators; only recorded statistics are used.");
        }
        return result;
    }

    public sealed record MatchLine(Guid PlayerId, DateOnly Date, int Level, bool Batted, int Runs, int BallsFaced, bool Dismissed, bool Bowled, int BallsBowled, int RunsConceded, int Wickets);

    private static string Describe(string role, IReadOnlyList<MatchLine> lines)
    {
        var runs = lines.Sum(l => l.Runs);
        var balls = lines.Sum(l => l.BallsFaced);
        var wk = lines.Sum(l => l.Wickets);
        var bb = lines.Sum(l => l.BallsBowled);
        var rc = lines.Sum(l => l.RunsConceded);
        var bat = $"{runs} runs at SR {(balls > 0 ? runs * 100m / balls : 0):0.0}";
        var bowl = $"{wk} wkts, econ {(bb > 0 ? rc * 6m / bb : 0):0.00}";
        return role switch { "bowler" => bowl, "allrounder" => $"{bat}; {bowl}", _ => bat };
    }

    private static decimal RoleIndex(string role, IReadOnlyList<MatchLine> lines) =>
        RoleIndexFromTotals(role, lines.Sum(l => l.Runs), lines.Sum(l => l.BallsFaced), lines.Count(l => l.Dismissed),
            lines.Sum(l => l.BallsBowled), lines.Sum(l => l.RunsConceded), lines.Sum(l => l.Wickets));

    /// <summary>0–100 index from batting average/strike rate and bowling economy/strike rate, weighted by role.</summary>
    public static decimal RoleIndexFromTotals(string role, int runs, int balls, int outs, int ballsBowled, int conceded, int wickets)
    {
        var avg = runs / (decimal)Math.Max(1, outs);
        var sr = balls > 0 ? runs * 100m / balls : 0;
        var bat = balls < 30 ? 30 : Math.Clamp(avg / 40m * 50 + (sr - 100) / 60m * 50, 0, 100);
        var econ = ballsBowled > 0 ? conceded * 6m / ballsBowled : 12;
        var bowlSr = wickets > 0 ? ballsBowled / (decimal)wickets : 60;
        var bowl = ballsBowled < 60 ? 30 : Math.Clamp((10 - econ) / 4m * 50 + (40 - bowlSr) / 24m * 50, 0, 100);
        return role switch
        {
            "bowler" => bowl,
            "allrounder" => Math.Clamp(Math.Max(bat, bowl) * 0.6m + Math.Min(bat, bowl) * 0.6m, 0, 100),
            _ => bat,
        };
    }

    /// <summary>
    /// Team strength = average rating of the best XI that respects the overseas limit, with penalties for
    /// missing a wicketkeeper or having fewer bowling options than the season rule requires.
    /// </summary>
    public static TeamStrengthDto TeamStrength(IReadOnlyList<(Guid Id, decimal Rating, bool Overseas, bool Keeper, bool CanBowl)> squad, int maxOverseas, int minBowlers)
    {
        var xi = new List<(Guid Id, decimal Rating, bool Overseas, bool Keeper, bool CanBowl)>();
        List<string> notes = [];
        var ordered = squad.OrderByDescending(p => p.Rating).ToList();
        var keeper = ordered.FirstOrDefault(p => p.Keeper);
        if (keeper.Id != Guid.Empty) xi.Add(keeper);
        else notes.Add("No wicketkeeper in the squad (−5).");
        foreach (var p in ordered.Where(p => p.CanBowl))
        {
            if (xi.Count(x => x.CanBowl) >= minBowlers) break;
            if (xi.Any(x => x.Id == p.Id) || (p.Overseas && xi.Count(x => x.Overseas) >= maxOverseas)) continue;
            xi.Add(p);
        }
        foreach (var p in ordered)
        {
            if (xi.Count >= 11) break;
            if (xi.Any(x => x.Id == p.Id) || (p.Overseas && xi.Count(x => x.Overseas) >= maxOverseas)) continue;
            xi.Add(p);
        }
        var strength = xi.Count == 0 ? 0 : xi.Average(p => p.Rating) * Math.Min(1, xi.Count / 11m);
        if (!xi.Any(p => p.Keeper)) strength -= 5;
        var bowlers = xi.Count(p => p.CanBowl);
        if (bowlers < minBowlers)
        {
            strength -= (minBowlers - bowlers) * 2;
            notes.Add($"Only {bowlers} bowling options (rule needs {minBowlers}).");
        }
        if (xi.Count < 11) notes.Add($"Only {xi.Count} eligible players for an XI.");
        return new TeamStrengthDto(Math.Round(Math.Max(0, strength), 1), xi.Select(p => p.Id).ToList(), xi.Count(p => p.Overseas), notes);
    }
}

public class ValuationService(IAppDbContext db, ICurrentUser me, IClock clock, RatingService ratings, IRulesService rules, IAuditService audit)
{
    public const string Disclaimer = "Model-generated estimate based on recorded statistics and past prices. Not an official price or offer.";

    public async Task<ValuationDto> LatestAsync(Guid playerId, CancellationToken ct)
    {
        me.Require(Perm.ValuationRead);
        var season = await rules.CurrentSeasonAsync(ct);
        var latest = await db.PlayerValuations.AsNoTracking().Include(v => v.Player)
            .Where(v => v.PlayerId == playerId && v.SeasonId == season.Id).OrderByDescending(v => v.CreatedAt).FirstOrDefaultAsync(ct);
        if (latest is not null && latest.ModelVersion == ValuationModel.Version && latest.CreatedAt > clock.UtcNow.AddDays(-1)) return ToDto(latest);
        return await RunAsync(playerId, ct);
    }

    public async Task<ValuationDto> RunAsync(Guid playerId, CancellationToken ct)
    {
        var result = (await RunManyAsync([playerId], ct)).FirstOrDefault() ?? throw new NotFoundException("Player");
        return result;
    }

    public async Task<IReadOnlyList<ValuationDto>> RunManyAsync(IReadOnlyCollection<Guid> playerIds, CancellationToken ct)
    {
        me.Require(Perm.ValuationRead);
        var season = await rules.CurrentSeasonAsync(ct);
        var seasonRules = await rules.ForSeasonAsync(season.Id, ct);
        var purse = seasonRules.Decimal(RuleKeys.AuctionPurse);
        var minBase = seasonRules.Decimal(RuleKeys.MinBasePrice);
        var rated = await ratings.RateManyAsync(playerIds, ct);
        var lastPrices = await db.PlayerTeams.AsNoTracking().Where(t => playerIds.Contains(t.PlayerId) && t.Price != null)
            .GroupBy(t => t.PlayerId).Select(g => new { g.Key, Price = g.OrderByDescending(t => t.Season!.Year).Select(t => t.Price).First() })
            .ToDictionaryAsync(x => x.Key, x => x.Price, ct);
        // Demand: how scarce the player's role is among players available in auctions vs how many franchises lack it.
        var roleSupply = await db.Players.AsNoTracking().Where(p => p.Status == PlayerStatus.AvailableForAuction)
            .GroupBy(p => p.PrimaryRole!.Name).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var history = await db.AuctionResults.AsNoTracking().Where(r => r.Outcome == AuctionOutcome.Sold)
            .Select(r => new { Role = r.Player!.PrimaryRole!.Name, r.Price }).ToListAsync(ct);
        var roleAvgPrice = history.GroupBy(h => h.Role).ToDictionary(g => g.Key, g => g.Average(h => h.Price ?? 0));

        var now = clock.UtcNow;
        List<ValuationDto> results = [];
        foreach (var (id, rating) in rated)
        {
            var est = ValuationModel.Estimate(rating, lastPrices.GetValueOrDefault(id), roleSupply.GetValueOrDefault(rating.Role),
                roleAvgPrice.TryGetValue(rating.Role, out var avg) ? avg : null, purse, minBase);
            var entity = new PlayerValuation
            {
                PlayerId = id, SeasonId = season.Id, LowEstimate = est.Low, MidEstimate = est.Mid, HighEstimate = est.High,
                DemandIndex = est.Demand, LastKnownPrice = lastPrices.GetValueOrDefault(id), ModelVersion = ValuationModel.Version,
                Explanation = JsonSerializer.Serialize(new { est.Factors, rating.Rating }, JsonOptions.Default), CreatedAt = now,
            };
            db.PlayerValuations.Add(entity);
            results.Add(new ValuationDto(id, rating.PlayerName, est.Low, est.Mid, est.High, est.Demand, ValuationModel.DemandLabel(est.Demand),
                entity.LastKnownPrice, rating.Rating, est.Factors, ValuationModel.Version, now, Disclaimer));
        }
        if (playerIds.Count <= 5) audit.Record("valuation.run", "Player", string.Join(",", playerIds), null);
        await db.SaveChangesAsync(ct);
        return results;
    }

    private static ValuationDto ToDto(PlayerValuation v)
    {
        using var doc = JsonDocument.Parse(v.Explanation);
        var factors = doc.RootElement.GetProperty("factors").Deserialize<List<ValuationFactorDto>>(JsonOptions.Default) ?? [];
        var rating = doc.RootElement.GetProperty("rating").GetDecimal();
        return new ValuationDto(v.PlayerId, v.Player?.FullName ?? "", v.LowEstimate, v.MidEstimate, v.HighEstimate, v.DemandIndex,
            ValuationModel.DemandLabel(v.DemandIndex), v.LastKnownPrice, rating, factors, v.ModelVersion, v.CreatedAt, Disclaimer);
    }
}

/// <summary>Pure valuation formula (unit tested). Prices are rounded to the nearest ₹5 lakh.</summary>
public static class ValuationModel
{
    public const string Version = "valuation-v1";

    public sealed record Result(decimal Low, decimal Mid, decimal High, int Demand, IReadOnlyList<ValuationFactorDto> Factors);

    public static Result Estimate(PlayerRatingDto rating, decimal? lastPrice, int roleSupply, decimal? roleAveragePrice, decimal purse, decimal minBase)
    {
        List<ValuationFactorDto> factors = [];
        // Rating → share of purse: a 90-rated player is worth ~13% of a purse, a 40-rated player the minimum.
        var share = rating.Rating <= 40 ? 0 : (decimal)Math.Pow((double)((rating.Rating - 40) / 50m), 2.2) * 0.13m;
        var fromRating = Math.Max(minBase, purse * share);
        factors.Add(new ValuationFactorDto("Player rating", $"{rating.Rating:0.0}/100", $"Rating maps to about {Money.Format(fromRating)}"));

        var mid = fromRating;
        if (lastPrice is { } lp && lp > 0)
        {
            mid = fromRating * 0.6m + lp * 0.4m;
            factors.Add(new ValuationFactorDto("Previous price", Money.Format(lp), "40% weight on the last recorded price"));
        }
        if (roleAveragePrice is { } rap && rap > 0)
        {
            mid = mid * 0.85m + rap * 0.15m;
            factors.Add(new ValuationFactorDto("Market trend", Money.Format(rap), $"Average price paid for {rating.Role}s in recorded auctions (15% weight)"));
        }
        var demand = (int)Math.Clamp(rating.Rating + (roleSupply < 10 ? 15 : roleSupply < 25 ? 5 : -5) + (rating.IsOverseas ? -5 : 5), 0, 100);
        var demandFactor = 0.85m + demand / 100m * 0.3m;
        mid *= demandFactor;
        factors.Add(new ValuationFactorDto("Role demand", $"{demand}/100", $"{roleSupply} {rating.Role}s available in the pool"));
        if (rating.Age >= 33)
        {
            mid *= 0.85m;
            factors.Add(new ValuationFactorDto("Age", $"{rating.Age}", "Shorter remaining career (−15%)"));
        }
        var availability = rating.Factors.FirstOrDefault(f => f.Factor == "Availability");
        if (availability is { Score: < 100 })
        {
            mid *= 0.8m;
            factors.Add(new ValuationFactorDto("Availability", availability.Explanation, "Availability concern (−20%)"));
        }
        var spread = rating.Confidence switch { "High" => 0.15m, "Medium" => 0.25m, _ => 0.40m };
        factors.Add(new ValuationFactorDto("Data confidence", rating.Confidence, $"Range ±{spread:P0} ({rating.MatchesUsed} matches)"));

        mid = Math.Max(minBase, Round(mid));
        return new Result(Math.Max(minBase, Round(mid * (1 - spread))), mid, Round(mid * (1 + spread)), demand, factors);
    }

    public static string DemandLabel(int demand) => demand switch { >= 80 => "Very high", >= 60 => "High", >= 40 => "Moderate", _ => "Low" };

    private static decimal Round(decimal amount) => Math.Round(amount / (5 * Money.Lakh)) * 5 * Money.Lakh;
}
