namespace Ipl.Domain;

// ---------------- Contracts & finance ----------------

public enum ContractStatus { Active, Expired, Terminated, Transferred }

public class PlayerContract : AuditableEntity, IFranchiseOwned
{
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public Guid FranchiseId { get; set; }
    public Franchise? Franchise { get; set; }
    public int StartSeasonId { get; set; }
    public int EndSeasonId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal AnnualSalary { get; set; }
    public AcquisitionType Acquisition { get; set; }
    public ContractStatus Status { get; set; }
    public string? Notes { get; set; }
    public List<ContractPayment> Payments { get; set; } = [];
}

public enum PaymentStatus { Scheduled, Paid, Cancelled }

public class ContractPayment : Entity
{
    public Guid PlayerContractId { get; set; }
    public PlayerContract? Contract { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime? PaidAt { get; set; }
    public Guid? FinanceTransactionId { get; set; }
}

/// <summary>Separate ledgers: auction purse, player compensation, trade consideration and other expenses.</summary>
public enum FinanceCategory { AuctionPurse, PlayerCompensation, TradeConsideration, OtherExpense }

public enum FinanceDirection { Debit, Credit }

/// <summary>Every money movement. Immutable once written; corrections are new transactions.</summary>
public class FinanceTransaction : Entity, IFranchiseOwned
{
    public Guid FranchiseId { get; set; }
    public int SeasonId { get; set; }
    public FinanceCategory Category { get; set; }
    public FinanceDirection Direction { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Purse balance after this transaction (AuctionPurse category only).</summary>
    public decimal? PurseBalanceAfter { get; set; }
    public string Description { get; set; } = "";
    public string? ReferenceType { get; set; }
    public string? ReferenceId { get; set; }
    /// <summary>Unique: the same business event can never be booked twice.</summary>
    public string IdempotencyKey { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public class BudgetAllocation : Entity, IFranchiseOwned
{
    public Guid FranchiseId { get; set; }
    public int SeasonId { get; set; }
    public FinanceCategory Category { get; set; }
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ---------------- Scouting, valuation, comparison ----------------

public enum AssignmentStatus { Open, InProgress, Completed, Cancelled }

public class ScoutingAssignment : AuditableEntity, IFranchiseOwned
{
    public Guid FranchiseId { get; set; }
    public Guid? PlayerId { get; set; }
    public Player? Player { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid AssignedToUserId { get; set; }
    public User? AssignedTo { get; set; }
    public DateOnly? DueDate { get; set; }
    public AssignmentStatus Status { get; set; }
}

public enum ScoutingRecommendation { Sign, StrongMonitor, Monitor, Pass }

public enum ReportStatus { Draft, Submitted, Reviewed }

public class PlayerScoutingReport : AuditableEntity, IFranchiseOwned, ISoftDelete
{
    public Guid FranchiseId { get; set; }
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public Guid ScoutUserId { get; set; }
    public User? Scout { get; set; }
    public Guid? AssignmentId { get; set; }
    public string MatchObserved { get; set; } = "";
    public DateOnly ObservedOn { get; set; }
    public string Strengths { get; set; } = "";
    public string Weaknesses { get; set; } = "";
    public string Observations { get; set; } = "";
    /// <summary>Scout ratings, 1–10.</summary>
    public int TechniqueRating { get; set; }
    public int TemperamentRating { get; set; }
    public int FitnessRating { get; set; }
    public int PotentialRating { get; set; }
    public int OverallRating { get; set; }
    public ScoutingRecommendation Recommendation { get; set; }
    public ReportStatus Status { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>A model-generated valuation estimate (never an official price), with its inputs.</summary>
public class PlayerValuation : Entity
{
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public int SeasonId { get; set; }
    public decimal LowEstimate { get; set; }
    public decimal MidEstimate { get; set; }
    public decimal HighEstimate { get; set; }
    /// <summary>0–100 demand indicator.</summary>
    public int DemandIndex { get; set; }
    public decimal? LastKnownPrice { get; set; }
    public string ModelVersion { get; set; } = "";
    /// <summary>JSON list of factors and how much each contributed.</summary>
    public string Explanation { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class PlayerComparison : AuditableEntity, IFranchiseOwned
{
    public Guid FranchiseId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>JSON array of player ids.</summary>
    public string PlayerIds { get; set; } = "[]";
    public string? Notes { get; set; }
    /// <summary>Visible to the whole franchise (otherwise only the author).</summary>
    public bool IsShared { get; set; }
}

// ---------------- Medical ----------------

public enum InjurySeverity { Minor, Moderate, Major, SeasonEnding }

/// <summary>Detailed medical data: only users with medical.read can see these rows.</summary>
public class PlayerMedicalRecord : AuditableEntity, IFranchiseOwned
{
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public Guid FranchiseId { get; set; }
    public string Condition { get; set; } = "";
    public string BodyPart { get; set; } = "";
    public InjurySeverity Severity { get; set; }
    public string Assessment { get; set; } = "";
    public DateOnly InjuryDate { get; set; }
    public DateOnly? ExpectedReturn { get; set; }
    public int RecoveryProgress { get; set; }
    public bool FitnessCleared { get; set; }
    public DateTime? ClearedAt { get; set; }
    public Guid? ClearedBy { get; set; }
}

// ---------------- Communication ----------------

public enum ThreadType { Internal, InterFranchise, Trade }

public class MessageThread : AuditableEntity
{
    public string Subject { get; set; } = "";
    public ThreadType Type { get; set; }
    public Guid FranchiseId { get; set; }
    /// <summary>The other franchise for inter-franchise and trade threads.</summary>
    public Guid? CounterpartyFranchiseId { get; set; }
    public Guid? TradeOfferId { get; set; }
    public DateTime LastMessageAt { get; set; }
    public List<Message> Messages { get; set; } = [];
}

public class Message : Entity
{
    public Guid ThreadId { get; set; }
    public MessageThread? Thread { get; set; }
    public Guid SenderUserId { get; set; }
    public string SenderName { get; set; } = "";
    public Guid SenderFranchiseId { get; set; }
    public string Body { get; set; } = "";
    public DateTime SentAt { get; set; }
}

public class MessageThreadRead
{
    public Guid ThreadId { get; set; }
    public Guid UserId { get; set; }
    public DateTime LastReadAt { get; set; }
}

public class Notification : Entity
{
    public Guid UserId { get; set; }
    public Guid? FranchiseId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Link { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    /// <summary>Prevents the same reminder being sent twice.</summary>
    public string? DedupKey { get; set; }
}

public class NotificationPreference
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public bool InApp { get; set; } = true;
    public bool Email { get; set; }
}

// ---------------- Documents ----------------

/// <summary>Uploaded files are stored in the database and only served through permission checks.</summary>
public class Document : Entity, IFranchiseOwned, ISoftDelete
{
    public Guid FranchiseId { get; set; }
    public string OwnerType { get; set; } = "";
    public Guid OwnerId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public byte[] Content { get; set; } = [];
    public bool IsConfidential { get; set; } = true;
    public Guid UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
