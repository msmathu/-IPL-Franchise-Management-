namespace Ipl.Domain;

public enum TradeStatus
{
    Draft,
    Submitted,
    PendingResponse,
    UnderNegotiation,
    CounterofferReceived,
    AcceptedInPrinciple,
    AwaitingPlayerConsent,
    AwaitingLeagueApproval,
    Approved,
    Rejected,
    Withdrawn,
    Completed,
    Expired,
}

public enum TradeType { Cash, Swap, PlayersPlusCash }

/// <summary>
/// A trade between two franchises: any number of players each way plus an optional trade consideration.
/// The offer always holds the latest terms; every revision is kept in TradeNegotiation.
/// </summary>
public class TradeOffer : AuditableEntity
{
    public string Reference { get; set; } = "";
    public int SeasonId { get; set; }
    public Season? Season { get; set; }
    public Guid InitiatorFranchiseId { get; set; }
    public Franchise? InitiatorFranchise { get; set; }
    public Guid CounterpartyFranchiseId { get; set; }
    public Franchise? CounterpartyFranchise { get; set; }
    public TradeStatus Status { get; set; }
    /// <summary>Trade consideration (not salary, not purse) and who pays it.</summary>
    public decimal CashConsideration { get; set; }
    public Guid? CashPayerFranchiseId { get; set; }
    public string? Conditions { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>The franchise that must respond next while negotiating.</summary>
    public Guid? AwaitingFranchiseId { get; set; }
    public int RevisionNo { get; set; } = 1;
    public byte[] RowVersion { get; set; } = [];

    public List<TradeOfferPlayer> Players { get; set; } = [];
    public List<TradeNegotiation> Negotiations { get; set; } = [];
    public List<TradeApproval> Approvals { get; set; } = [];

    public TradeType Type => Players.Count == 0 ? TradeType.Cash
        : CashConsideration > 0 ? TradeType.PlayersPlusCash
        : Players.Any(p => p.FromFranchiseId == InitiatorFranchiseId) && Players.Any(p => p.FromFranchiseId == CounterpartyFranchiseId)
            ? TradeType.Swap : TradeType.Cash;

    public bool IsParty(Guid franchiseId) => franchiseId == InitiatorFranchiseId || franchiseId == CounterpartyFranchiseId;

    public Guid OtherParty(Guid franchiseId) => franchiseId == InitiatorFranchiseId ? CounterpartyFranchiseId : InitiatorFranchiseId;

    public bool IsOpen => Status is TradeStatus.Draft or TradeStatus.Submitted or TradeStatus.PendingResponse
        or TradeStatus.UnderNegotiation or TradeStatus.CounterofferReceived or TradeStatus.AcceptedInPrinciple
        or TradeStatus.AwaitingPlayerConsent or TradeStatus.AwaitingLeagueApproval or TradeStatus.Approved;

    /// <summary>Terms can change only before both sides accept in principle.</summary>
    public bool IsNegotiable => Status is TradeStatus.Draft or TradeStatus.PendingResponse
        or TradeStatus.UnderNegotiation or TradeStatus.CounterofferReceived;

    /// <summary>
    /// Works out the status once both franchises accepted: franchise authorisations first, then
    /// player consent, then league approval. Any rejection rejects the trade.
    /// </summary>
    public TradeStatus StatusFromApprovals()
    {
        if (Approvals.Any(a => a.Decision == ApprovalDecision.Rejected)) return TradeStatus.Rejected;
        bool Pending(ApprovalType t) => Approvals.Any(a => a.Type == t && a.Decision == ApprovalDecision.Pending);
        if (Pending(ApprovalType.FranchiseAuthorization)) return TradeStatus.AcceptedInPrinciple;
        if (Pending(ApprovalType.PlayerConsent)) return TradeStatus.AwaitingPlayerConsent;
        if (Pending(ApprovalType.LeagueApproval)) return TradeStatus.AwaitingLeagueApproval;
        return TradeStatus.Approved;
    }
}

public class TradeOfferPlayer : Entity
{
    public Guid TradeOfferId { get; set; }
    public TradeOffer? TradeOffer { get; set; }
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public Guid FromFranchiseId { get; set; }
    public Guid ToFranchiseId { get; set; }
    /// <summary>Annual contract salary when the player was added (for the impact calculator).</summary>
    public decimal ContractSalary { get; set; }
}

public enum NegotiationAction { Created, Submitted, Countered, Accepted, Rejected, Withdrawn, Message, Note, Expired, Approved, Completed }

/// <summary>Negotiation history: each counteroffer stores a snapshot of the terms.</summary>
public class TradeNegotiation : Entity
{
    public Guid TradeOfferId { get; set; }
    public TradeOffer? TradeOffer { get; set; }
    public int RevisionNo { get; set; }
    public NegotiationAction Action { get; set; }
    public Guid? ByFranchiseId { get; set; }
    public Guid? ByUserId { get; set; }
    public string? ByUserName { get; set; }
    public string? Message { get; set; }
    /// <summary>Internal notes are only visible to the author's franchise.</summary>
    public bool IsInternal { get; set; }
    public string? TermsSnapshot { get; set; }
    public DateTime At { get; set; }
}

public enum ApprovalType { FranchiseAuthorization, PlayerConsent, LeagueApproval }

public enum ApprovalDecision { Pending, Approved, Rejected }

public class TradeApproval : Entity
{
    public Guid TradeOfferId { get; set; }
    public TradeOffer? TradeOffer { get; set; }
    public ApprovalType Type { get; set; }
    public Guid? FranchiseId { get; set; }
    public Guid? PlayerId { get; set; }
    public ApprovalDecision Decision { get; set; }
    public Guid? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Comment { get; set; }
}

/// <summary>The official, completed transfer. One per trade (unique), written in the same DB transaction.</summary>
public class TradeTransaction : Entity
{
    public Guid TradeOfferId { get; set; }
    public TradeOffer? TradeOffer { get; set; }
    public int SeasonId { get; set; }
    public DateTime CompletedAt { get; set; }
    public Guid CompletedBy { get; set; }
    public string Summary { get; set; } = "";
}
