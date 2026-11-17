namespace Ipl.Domain;

public enum AuctionStatus { Draft, Ready, Live, Paused, Completed }

public enum AuctionLotStatus { Pending, OnBlock, Sold, Unsold, Withdrawn }

public class AuctionEvent : AuditableEntity
{
    public int SeasonId { get; set; }
    public Season? Season { get; set; }
    public string Name { get; set; } = "";
    public bool IsMegaAuction { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public AuctionStatus Status { get; set; }
    public Guid? CurrentLotId { get; set; }
    /// <summary>Seconds without a new bid before the auctioneer is prompted to close the lot (0 = off).</summary>
    public int BidTimerSeconds { get; set; } = 30;
    public bool AllowReopenUnsold { get; set; } = true;
    public int CurrentRound { get; set; } = 1;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<AuctionPlayer> Lots { get; set; } = [];
}

/// <summary>A player in an auction's pool (a "lot").</summary>
public class AuctionPlayer : Entity
{
    public Guid AuctionEventId { get; set; }
    public AuctionEvent? AuctionEvent { get; set; }
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public string SetName { get; set; } = "";
    public int SetNumber { get; set; }
    public int LotNumber { get; set; }
    public decimal BasePrice { get; set; }
    public AuctionLotStatus Status { get; set; }
    public decimal? CurrentBid { get; set; }
    public Guid? CurrentBidFranchiseId { get; set; }
    public DateTime? LastBidAt { get; set; }
    public int Round { get; set; } = 1;
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public class AuctionBid : Entity
{
    public Guid AuctionPlayerId { get; set; }
    public AuctionPlayer? AuctionPlayer { get; set; }
    public Guid FranchiseId { get; set; }
    public Franchise? Franchise { get; set; }
    public decimal Amount { get; set; }
    public Guid PlacedBy { get; set; }
    public DateTime PlacedAt { get; set; }
    /// <summary>Client-generated key so a retried request can't place the same bid twice.</summary>
    public string IdempotencyKey { get; set; } = "";
}

public enum AuctionOutcome { Sold, Unsold }

public class AuctionResult : Entity
{
    public Guid AuctionEventId { get; set; }
    public Guid AuctionPlayerId { get; set; }
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public Guid? FranchiseId { get; set; }
    public Franchise? Franchise { get; set; }
    public AuctionOutcome Outcome { get; set; }
    public decimal? Price { get; set; }
    public int Round { get; set; }
    public DateTime At { get; set; }
}
