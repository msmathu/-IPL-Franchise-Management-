using System.Text;
using Ipl.Application.Analytics;
using Ipl.Application.Players;
using Ipl.Application.Rules;
using Ipl.Application.Squads;
using Ipl.Application.Statistics;
using Ipl.Domain;

namespace Ipl.Tests;

public class BidRuleTests
{
    private static readonly BidIncrementTier[] Tiers =
    [
        new(Money.Crore, 5 * Money.Lakh), new(2 * Money.Crore, 10 * Money.Lakh), new(5 * Money.Crore, 20 * Money.Lakh), new(null, 25 * Money.Lakh),
    ];

    [Fact]
    public void First_bid_is_the_base_price() => Assert.Equal(50 * Money.Lakh, BidRules.NextBid(50 * Money.Lakh, null, Tiers));

    [Theory]
    [InlineData(50, 55)]     // below 1 Cr: +5 L
    [InlineData(100, 110)]   // 1–2 Cr: +10 L
    [InlineData(250, 270)]   // 2–5 Cr: +20 L
    [InlineData(500, 525)]   // above 5 Cr: +25 L
    public void Increments_follow_the_configured_tiers(int currentLakh, int expectedLakh) =>
        Assert.Equal(expectedLakh * Money.Lakh, BidRules.NextBid(20 * Money.Lakh, currentLakh * Money.Lakh, Tiers));

    [Fact]
    public void Missing_tiers_are_a_rule_error() =>
        Assert.Throws<DomainException>(() => BidRules.NextBid(Money.Crore, Money.Crore, [new BidIncrementTier(Money.Lakh, Money.Lakh)]));
}

public class RetentionSimulationTests
{
    private static SquadMember M(string name, bool capped, bool overseas, string availability = "Available", int age = 28) =>
        new(Guid.NewGuid(), name, "batter", "Batter", false, overseas, capped, false, Money.Crore, Money.Crore, age, availability, "Auction");

    private static readonly RetentionRulesDto Rules = new(null, 4, 3, 2, 1, [18 * Money.Crore, 14 * Money.Crore, 11 * Money.Crore], 4 * Money.Crore,
        120 * Money.Crore, 25, 8);

    [Fact]
    public void Capped_slots_cost_in_order_and_uncapped_cost_flat()
    {
        var squad = new[] { M("A", true, false), M("B", true, false), M("C", false, false) };
        var sim = RetentionService.Simulate(squad.Select(s => s.PlayerId).ToList(), squad, Rules);
        Assert.True(sim.IsValid);
        Assert.Equal([18 * Money.Crore, 14 * Money.Crore, 4 * Money.Crore], sim.Slots.Select(s => s.Cost));
        Assert.Equal(120 * Money.Crore - 36 * Money.Crore, sim.PurseAfter);
        Assert.Equal(22, sim.SquadSlotsRemaining);
    }

    [Fact]
    public void Limits_are_enforced()
    {
        var squad = new[] { M("A", true, true), M("B", true, true), M("C", true, false), M("D", true, false), M("E", false, false) };
        var sim = RetentionService.Simulate(squad.Select(s => s.PlayerId).ToList(), squad, Rules);
        Assert.False(sim.IsValid);
        Assert.Contains(sim.Violations, v => v.Contains("retentions selected"));
        Assert.Contains(sim.Violations, v => v.Contains("capped players"));
        Assert.Contains(sim.Violations, v => v.Contains("overseas"));
    }

    [Fact]
    public void Injured_players_produce_a_warning_not_a_violation()
    {
        var squad = new[] { M("A", true, false, "Injured") };
        var sim = RetentionService.Simulate([squad[0].PlayerId], squad, Rules);
        Assert.True(sim.IsValid);
        Assert.Single(sim.Warnings);
    }
}

public class TradeWorkflowTests
{
    private static TradeOffer Offer(params (ApprovalType Type, ApprovalDecision Decision)[] approvals) =>
        new() { Approvals = approvals.Select(a => new TradeApproval { Type = a.Type, Decision = a.Decision }).ToList() };

    [Fact]
    public void Franchise_authorisations_come_before_player_consent_and_league_approval()
    {
        Assert.Equal(TradeStatus.AcceptedInPrinciple, Offer((ApprovalType.FranchiseAuthorization, ApprovalDecision.Pending), (ApprovalType.PlayerConsent, ApprovalDecision.Pending)).StatusFromApprovals());
        Assert.Equal(TradeStatus.AwaitingPlayerConsent, Offer((ApprovalType.FranchiseAuthorization, ApprovalDecision.Approved), (ApprovalType.PlayerConsent, ApprovalDecision.Pending), (ApprovalType.LeagueApproval, ApprovalDecision.Pending)).StatusFromApprovals());
        Assert.Equal(TradeStatus.AwaitingLeagueApproval, Offer((ApprovalType.FranchiseAuthorization, ApprovalDecision.Approved), (ApprovalType.PlayerConsent, ApprovalDecision.Approved), (ApprovalType.LeagueApproval, ApprovalDecision.Pending)).StatusFromApprovals());
        Assert.Equal(TradeStatus.Approved, Offer((ApprovalType.FranchiseAuthorization, ApprovalDecision.Approved), (ApprovalType.LeagueApproval, ApprovalDecision.Approved)).StatusFromApprovals());
    }

    [Fact]
    public void Any_rejection_rejects_the_trade() =>
        Assert.Equal(TradeStatus.Rejected, Offer((ApprovalType.FranchiseAuthorization, ApprovalDecision.Approved), (ApprovalType.PlayerConsent, ApprovalDecision.Rejected)).StatusFromApprovals());

    [Fact]
    public void Completed_trades_are_not_negotiable_or_open()
    {
        var t = new TradeOffer { Status = TradeStatus.Completed };
        Assert.False(t.IsNegotiable);
        Assert.False(t.IsOpen);
    }
}

public class ValuationAndStrengthTests
{
    private static PlayerRatingDto Rating(decimal rating, string confidence = "High", int age = 27, decimal availability = 100) => new(
        Guid.NewGuid(), "Test", "Batter", age, false, rating, confidence, 40,
        [new RatingFactorDto("Availability", availability, 0.05m, 5, "Public status")], "test");

    [Fact]
    public void Higher_ratings_are_worth_more_and_ranges_are_ordered()
    {
        var low = ValuationModel.Estimate(Rating(50), null, 30, null, 120 * Money.Crore, 20 * Money.Lakh);
        var high = ValuationModel.Estimate(Rating(85), null, 30, null, 120 * Money.Crore, 20 * Money.Lakh);
        Assert.True(high.Mid > low.Mid);
        Assert.True(high.Low <= high.Mid && high.Mid <= high.High);
        Assert.Equal(0, high.Mid % (5 * Money.Lakh)); // rounded to ₹5 lakh
    }

    [Fact]
    public void Never_below_minimum_base_price_and_low_confidence_widens_the_range()
    {
        var weak = ValuationModel.Estimate(Rating(20, "Low"), null, 30, null, 120 * Money.Crore, 20 * Money.Lakh);
        Assert.True(weak.Low >= 20 * Money.Lakh);
        var sure = ValuationModel.Estimate(Rating(80, "High"), null, 30, null, 120 * Money.Crore, 20 * Money.Lakh);
        var unsure = ValuationModel.Estimate(Rating(80, "Low"), null, 30, null, 120 * Money.Crore, 20 * Money.Lakh);
        Assert.True(unsure.High - unsure.Low > sure.High - sure.Low);
    }

    [Fact]
    public void Team_strength_respects_the_overseas_limit_and_penalises_no_keeper()
    {
        var squad = Enumerable.Range(0, 8).Select(i => (Guid.NewGuid(), 90m, true, false, i < 4))
            .Concat(Enumerable.Range(0, 10).Select(i => (Guid.NewGuid(), 50m, false, i == 0, i < 3))).ToList();
        var s = RatingService.TeamStrength(squad, 4, 5);
        Assert.Equal(11, s.BestXI.Count);
        Assert.Equal(4, s.OverseasInXI);
        var noKeeper = RatingService.TeamStrength(squad.Select(p => (p.Item1, p.Item2, p.Item3, false, p.Item5)).ToList(), 4, 5);
        Assert.True(noKeeper.Strength < s.Strength);
    }
}

public class RulesValidationTests
{
    private readonly RulesService service = new(null!, null!);

    [Fact]
    public void Valid_values_pass()
    {
        service.Validate(RuleKeys.MaxSquadSize, "25");
        service.Validate(RuleKeys.TradeRequiresLeagueApproval, "true");
        service.Validate(RuleKeys.BidIncrements, "[{\"upTo\":10000000,\"increment\":500000},{\"upTo\":null,\"increment\":2500000}]");
        service.Validate(RuleKeys.CappedRetentionCosts, "[180000000,140000000]");
    }

    [Theory]
    [InlineData(RuleKeys.MaxSquadSize, "-1")]
    [InlineData(RuleKeys.MaxSquadSize, "abc")]
    [InlineData(RuleKeys.TradeRequiresLeagueApproval, "maybe")]
    [InlineData(RuleKeys.BidIncrements, "[{\"upTo\":10000000,\"increment\":500000}]")] // last tier must be open-ended
    [InlineData(RuleKeys.BidIncrements, "not json")]
    [InlineData("unknown.rule", "1")]
    public void Invalid_values_are_rejected(string key, string value) =>
        Assert.Throws<Ipl.Application.AppValidationException>(() => service.Validate(key, value));
}

public class StatisticsAndCsvTests
{
    [Fact]
    public void Aggregation_counts_innings_not_outs_and_best_figures()
    {
        var lines = new List<PlayerMatchStatistic>
        {
            new() { Batted = true, Runs = 55, BallsFaced = 30, Dismissed = true, Fours = 5, Sixes = 2 },
            new() { Batted = true, Runs = 101, BallsFaced = 60, Dismissed = false },
            new() { Batted = false, Bowled = true, BallsBowled = 24, RunsConceded = 20, Wickets = 4 },
            new() { Batted = true, Runs = 0, BallsFaced = 2, Dismissed = true, Bowled = true, BallsBowled = 24, RunsConceded = 40, Wickets = 1 },
        };
        var row = new PlayerSeasonStatistic();
        StatisticsService.Aggregate(row, lines);
        Assert.Equal(4, row.Matches);
        Assert.Equal(3, row.Innings);
        Assert.Equal(1, row.NotOuts);
        Assert.Equal(101, row.HighestScore);
        Assert.True(row.HighestNotOut);
        Assert.Equal(1, row.Hundreds);
        Assert.Equal(1, row.Fifties);
        Assert.Equal("4/20", StatLineDto.From(row).BestBowling);
        Assert.Equal(1, row.FourWicketHauls);
        Assert.Equal(78m, StatLineDto.From(row).BattingAverage); // 156 runs / 2 dismissals
    }

    [Fact]
    public void Form_trend_needs_enough_matches()
    {
        Assert.Equal("Not enough recent matches", StatisticsService.TrendLabel(new FormSummaryDto(2, 50, null, null, 0, null, null), new FormSummaryDto(20, 400, null, null, 0, null, null)));
        Assert.Equal("Improving", StatisticsService.TrendLabel(new FormSummaryDto(5, 250, null, null, 0, null, null), new FormSummaryDto(20, 400, null, null, 0, null, null)));
    }

    [Fact]
    public void Csv_reader_handles_quotes_commas_and_newlines()
    {
        var rows = Csv.Read(new MemoryStream(Encoding.UTF8.GetBytes("code,full_name,notes\r\nP1,\"Smith, John\",\"line1\nline2\"\r\nP2,Jane,\"He said \"\"hi\"\"\"\r\n")));
        Assert.Equal(2, rows.Count);
        Assert.Equal("Smith, John", rows[0].Get("full_name"));
        Assert.Equal("line1\nline2", rows[0].Get("notes"));
        Assert.Equal("He said \"hi\"", rows[1].Get("notes"));
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-12.5", "-12.5")]
    [InlineData("a,b", "\"a,b\"")]
    public void Csv_export_neutralises_formulas(string input, string expected) => Assert.Equal(expected, Csv.Escape(input));
}
