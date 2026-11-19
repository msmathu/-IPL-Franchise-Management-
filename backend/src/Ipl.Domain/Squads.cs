namespace Ipl.Domain;

public enum SquadMemberStatus { Active, Released, TradedOut }

/// <summary>A player in a franchise's squad for a season (one active row per player per season).</summary>
public class FranchiseSquad : Entity, IFranchiseOwned
{
    public Guid FranchiseId { get; set; }
    public Franchise? Franchise { get; set; }
    public int SeasonId { get; set; }
    public Season? Season { get; set; }
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public AcquisitionType Acquisition { get; set; }
    public decimal Price { get; set; }
    public SquadMemberStatus Status { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
}

public class PlayingXI : AuditableEntity, IFranchiseOwned
{
    public Guid FranchiseId { get; set; }
    public int SeasonId { get; set; }
    public string Name { get; set; } = "";
    public string? Opponent { get; set; }
    public string? Venue { get; set; }
    public Guid? CaptainId { get; set; }
    public Guid? ViceCaptainId { get; set; }
    public Guid? WicketKeeperId { get; set; }
    public bool IsPreferred { get; set; }
    public string? Notes { get; set; }
    public List<PlayingXIPlayer> Players { get; set; } = [];
}

public class PlayingXIPlayer : Entity
{
    public Guid PlayingXIId { get; set; }
    public PlayingXI? PlayingXI { get; set; }
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    /// <summary>1–11 for the starting XI; null for impact-player substitutes.</summary>
    public int? BattingOrder { get; set; }
    public bool IsImpactSubstitute { get; set; }
    public bool IsBowlingOption { get; set; }
}

public enum RetentionCategory { Capped, Uncapped, RightToMatch }

public enum RetentionStatus { Draft, Submitted, Confirmed, Cancelled }

/// <summary>A named "what-if" retention combination; one plan can be submitted as final.</summary>
public class RetentionPlan : AuditableEntity, IFranchiseOwned
{
    public Guid FranchiseId { get; set; }
    public int SeasonId { get; set; }
    public string Name { get; set; } = "";
    public bool IsSubmitted { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public List<RetentionRecord> Records { get; set; } = [];
}

public class RetentionRecord : Entity, IFranchiseOwned
{
    public Guid? RetentionPlanId { get; set; }
    public RetentionPlan? RetentionPlan { get; set; }
    public int SeasonId { get; set; }
    public Guid FranchiseId { get; set; }
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public RetentionCategory Category { get; set; }
    public int SlotNumber { get; set; }
    public decimal Cost { get; set; }
    public RetentionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ReplacementStatus { Pending, Approved, Rejected, Cancelled }

/// <summary>A franchise's request to replace an injured squad player with an unsigned player (league approval).</summary>
public class ReplacementRequest : Entity, IFranchiseOwned
{
    public Guid FranchiseId { get; set; }
    public Franchise? Franchise { get; set; }
    public int SeasonId { get; set; }
    public Guid InjuredPlayerId { get; set; }
    public Player? InjuredPlayer { get; set; }
    public Guid ReplacementPlayerId { get; set; }
    public Player? ReplacementPlayer { get; set; }
    public decimal Fee { get; set; }
    public string? Reason { get; set; }
    public ReplacementStatus Status { get; set; }
    public DateTime RequestedAt { get; set; }
    public Guid? RequestedBy { get; set; }
    public string? DecisionNote { get; set; }
    public DateTime? DecidedAt { get; set; }
    public Guid? DecidedBy { get; set; }
}

public enum ReleaseStatus { Released, Cancelled }

public class ReleaseRecord : Entity, IFranchiseOwned
{
    public int SeasonId { get; set; }
    public Guid FranchiseId { get; set; }
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public string? Reason { get; set; }
    public ReleaseStatus Status { get; set; }
    public DateTime ReleasedAt { get; set; }
    public Guid? ReleasedBy { get; set; }
}
