namespace Ipl.Domain;

public enum SeasonStatus { Planning, Retention, Trading, Auction, InProgress, Completed }

public class Season
{
    public int Id { get; set; }
    public int Year { get; set; }
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public SeasonStatus Status { get; set; }
    public List<SeasonRule> Rules { get; set; } = [];
}

public enum RuleValueType { Integer, Decimal, Boolean, Text, Date, Json }

/// <summary>
/// One configurable rule for one season (purse, squad limits, retention costs, bid increments...).
/// Rules live in the database so each season can differ; nothing season-specific is hard-coded.
/// </summary>
public class SeasonRule
{
    public int Id { get; set; }
    public int SeasonId { get; set; }
    public Season? Season { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public RuleValueType ValueType { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class Franchise : AuditableEntity, ISoftDelete
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string City { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string PrimaryColor { get; set; } = "#1d4ed8";
    public string SecondaryColor { get; set; } = "#f59e0b";
    public string OwnerName { get; set; } = "";
    public string? OwnerCompany { get; set; }
    public string? OwnerEmail { get; set; }
    public string HomeGround { get; set; } = "";
    public int? FoundedYear { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Fictional sample franchise created for development/demo.</summary>
    public bool IsDemo { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<FranchiseSeason> Seasons { get; set; } = [];
    public List<FranchiseStaff> Staff { get; set; } = [];
}

/// <summary>A franchise's position, results and money for one season.</summary>
public class FranchiseSeason
{
    public int Id { get; set; }
    public Guid FranchiseId { get; set; }
    public Franchise? Franchise { get; set; }
    public int SeasonId { get; set; }
    public Season? Season { get; set; }
    public decimal PurseTotal { get; set; }
    /// <summary>Changed only through FinanceTransactions in the AuctionPurse category.</summary>
    public decimal PurseRemaining { get; set; }
    public decimal OperatingBudget { get; set; }
    public int? FinalPosition { get; set; }
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int NoResult { get; set; }
    public int Points { get; set; }
    public decimal? NetRunRate { get; set; }
    public bool IsChampion { get; set; }
    public bool IsRunnerUp { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public enum StaffCategory { Management, Coaching, Support }

/// <summary>Management and coaching staff (who may or may not be platform users).</summary>
public class FranchiseStaff : Entity
{
    public Guid FranchiseId { get; set; }
    public Franchise? Franchise { get; set; }
    public string Name { get; set; } = "";
    public string Position { get; set; } = "";
    public StaffCategory Category { get; set; }
    public int? SinceYear { get; set; }
    public Guid? UserId { get; set; }
}

/// <summary>A platform user who works for a franchise (their permissions come from UserRoles).</summary>
public class FranchiseMember : Entity
{
    public Guid FranchiseId { get; set; }
    public Franchise? Franchise { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string JobTitle { get; set; } = "";
    public DateTime JoinedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Key/value settings that are not season-specific (e.g. rating model weights).</summary>
public class SystemSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
