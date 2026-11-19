namespace Ipl.Domain;

/// <summary>
/// Names of the configurable season rules. Values are stored per season in SeasonRules; the
/// descriptions and types here are used to validate edits and render the settings screen.
/// </summary>
public static class RuleKeys
{
    // Squad
    public const string MaxSquadSize = "squad.max_size";
    public const string MinSquadSize = "squad.min_size";
    public const string MaxOverseasInSquad = "squad.max_overseas";
    public const string MaxOverseasInXI = "xi.max_overseas";
    public const string MinBowlingOptions = "xi.min_bowling_options";
    public const string ImpactPlayerEnabled = "xi.impact_player_enabled";
    public const string ImpactSubstitutes = "xi.impact_substitutes";

    // Purse & auction
    public const string AuctionPurse = "auction.purse";
    public const string BidIncrements = "auction.bid_increments";
    public const string MinBasePrice = "auction.min_base_price";
    public const string ReserveForMinimumSquad = "auction.reserve_for_min_squad";

    // Retention
    public const string RetentionDeadline = "retention.deadline";
    public const string MaxRetentions = "retention.max_total";
    public const string MaxCappedRetentions = "retention.max_capped";
    public const string MaxUncappedRetentions = "retention.max_uncapped";
    public const string MaxOverseasRetentions = "retention.max_overseas";
    public const string CappedRetentionCosts = "retention.capped_slot_costs";
    public const string UncappedRetentionCost = "retention.uncapped_cost";

    // Trading
    public const string TradeWindowOpen = "trade.window_open";
    public const string TradeWindowClose = "trade.window_close";
    public const string TradeOfferExpiryDays = "trade.offer_expiry_days";
    public const string TradeRequiresLeagueApproval = "trade.requires_league_approval";
    public const string TradeRequiresPlayerConsent = "trade.requires_player_consent";
    /// <summary>Whether a trade consideration changes the auction purse (official rules differ by season).</summary>
    public const string TradeConsiderationAffectsPurse = "trade.consideration_affects_purse";
    /// <summary>Whether a traded player's contract salary moves between purses.</summary>
    public const string TradeSalaryAffectsPurse = "trade.salary_affects_purse";

    // Injury replacements
    public const string ReplacementsEnabled = "replacement.enabled";
    public const string ReplacementFeeCap = "replacement.fee_capped_at_injured_fee";
    public const string ReplacementAffectsPurse = "replacement.fee_affects_purse";

    public static readonly (string Key, RuleValueType Type, string Category, string Description)[] Definitions =
    [
        (MaxSquadSize, RuleValueType.Integer, "Squad", "Maximum players in a squad"),
        (MinSquadSize, RuleValueType.Integer, "Squad", "Minimum players in a squad"),
        (MaxOverseasInSquad, RuleValueType.Integer, "Squad", "Maximum overseas players in a squad"),
        (MaxOverseasInXI, RuleValueType.Integer, "Playing XI", "Maximum overseas players in the Playing XI"),
        (MinBowlingOptions, RuleValueType.Integer, "Playing XI", "Minimum bowling options in the Playing XI"),
        (ImpactPlayerEnabled, RuleValueType.Boolean, "Playing XI", "Impact Player substitutes allowed"),
        (ImpactSubstitutes, RuleValueType.Integer, "Playing XI", "Number of substitutes to name"),
        (AuctionPurse, RuleValueType.Decimal, "Auction", "Auction purse per franchise (₹)"),
        (BidIncrements, RuleValueType.Json, "Auction", "Bid increment tiers: [{\"upTo\": amount, \"increment\": amount}] (last tier upTo null)"),
        (MinBasePrice, RuleValueType.Decimal, "Auction", "Lowest allowed base price (₹)"),
        (ReserveForMinimumSquad, RuleValueType.Boolean, "Auction", "Bids must leave enough purse to reach the minimum squad at base price"),
        (RetentionDeadline, RuleValueType.Date, "Retention", "Last day to submit retentions"),
        (MaxRetentions, RuleValueType.Integer, "Retention", "Maximum retained players"),
        (MaxCappedRetentions, RuleValueType.Integer, "Retention", "Maximum capped retentions"),
        (MaxUncappedRetentions, RuleValueType.Integer, "Retention", "Maximum uncapped retentions"),
        (MaxOverseasRetentions, RuleValueType.Integer, "Retention", "Maximum overseas retentions"),
        (CappedRetentionCosts, RuleValueType.Json, "Retention", "Purse deduction for each capped retention slot, in order: [amount, ...]"),
        (UncappedRetentionCost, RuleValueType.Decimal, "Retention", "Purse deduction per uncapped retention (₹)"),
        (TradeWindowOpen, RuleValueType.Date, "Trading", "Trade window opens"),
        (TradeWindowClose, RuleValueType.Date, "Trading", "Trade window closes"),
        (TradeOfferExpiryDays, RuleValueType.Integer, "Trading", "Days before an unanswered offer expires"),
        (TradeRequiresLeagueApproval, RuleValueType.Boolean, "Trading", "League approval required before a transfer is official"),
        (TradeRequiresPlayerConsent, RuleValueType.Boolean, "Trading", "Each traded player must consent"),
        (TradeConsiderationAffectsPurse, RuleValueType.Boolean, "Trading", "Trade consideration is added to/removed from the auction purse"),
        (TradeSalaryAffectsPurse, RuleValueType.Boolean, "Trading", "Traded players' contract salaries move between auction purses"),
        (ReplacementsEnabled, RuleValueType.Boolean, "Replacements", "Injured squad players may be replaced (league approval)"),
        (ReplacementFeeCap, RuleValueType.Boolean, "Replacements", "A replacement's fee may not exceed the injured player's fee"),
        (ReplacementAffectsPurse, RuleValueType.Boolean, "Replacements", "The replacement fee is deducted from the auction purse"),
    ];
}

/// <summary>One bid increment tier: bids up to <see cref="UpTo"/> rise by <see cref="Increment"/>.</summary>
public record BidIncrementTier(decimal? UpTo, decimal Increment);

public static class BidRules
{
    /// <summary>The next valid bid: base price for the first bid, otherwise current bid + the tier increment.</summary>
    public static decimal NextBid(decimal basePrice, decimal? currentBid, IReadOnlyList<BidIncrementTier> tiers)
    {
        if (currentBid is null) return basePrice;
        var tier = tiers.FirstOrDefault(t => t.UpTo is null || currentBid.Value < t.UpTo.Value)
            ?? throw new DomainException("auction.rules", "Bid increment rules are not configured.");
        return currentBid.Value + tier.Increment;
    }
}
