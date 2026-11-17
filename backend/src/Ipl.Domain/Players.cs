namespace Ipl.Domain;

public class Country
{
    public int Id { get; set; }
    /// <summary>ISO 3166-1 alpha-3 style code, e.g. IND, AUS.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Players from the league's home country are not overseas players.</summary>
    public bool IsHomeCountry { get; set; }
}

/// <summary>Lookup of playing roles (Batter, Bowler, All-rounder, Wicketkeeper...).</summary>
public class PlayerRoleType
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Batting, Bowling, AllRound or Keeping: used for squad balance.</summary>
    public string Group { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>Unknown = not published by the data source.</summary>
public enum BattingHand { RightHand, LeftHand, Unknown }

public enum BowlingType
{
    None,
    Fast,
    MediumFast,
    Medium,
    OffSpin,
    LegSpin,
    LeftArmOrthodox,
    LeftArmWristSpin,
    /// <summary>Bowls, but the style is not published by the data source.</summary>
    Unknown,
}

public enum PlayerStatus
{
    Active,
    Retained,
    Released,
    AvailableForAuction,
    UnderContract,
    Traded,
    Injured,
    Unavailable,
    Retired,
}

public enum CareerStatus { Active, RetiredInternational, Retired }

public class Player : AuditableEntity, ISoftDelete
{
    public string Code { get; set; } = "";
    public string FullName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Null when the data source does not publish it.</summary>
    public DateOnly? DateOfBirth { get; set; }
    public int CountryId { get; set; }
    public Country? Country { get; set; }
    public string? PhotoUrl { get; set; }
    public BattingHand BattingHand { get; set; }
    /// <summary>Free text as published, e.g. "Right-arm fast-medium".</summary>
    public string BowlingStyle { get; set; } = "";
    public BowlingType BowlingType { get; set; }
    public int PrimaryRoleId { get; set; }
    public PlayerRoleType? PrimaryRole { get; set; }
    public int? SecondaryRoleId { get; set; }
    public PlayerRoleType? SecondaryRole { get; set; }
    public bool IsWicketKeeper { get; set; }
    public bool IsOverseas { get; set; }
    /// <summary>Has played senior international cricket.</summary>
    public bool IsCapped { get; set; }
    public Guid? CurrentFranchiseId { get; set; }
    public Franchise? CurrentFranchise { get; set; }
    public PlayerStatus Status { get; set; }
    public CareerStatus CareerStatus { get; set; }
    /// <summary>Fictional sample player created for development/demo.</summary>
    public bool IsDemo { get; set; }
    /// <summary>Where the profile came from (provider name, CSV import, demo generator).</summary>
    public string DataSource { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public enum AcquisitionType { Auction, Retained, Trade, Replacement, Draft, Initial }

/// <summary>Which franchise a player belonged to in which season.</summary>
public class PlayerTeam : Entity
{
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public Guid FranchiseId { get; set; }
    public Franchise? Franchise { get; set; }
    public int SeasonId { get; set; }
    public Season? Season { get; set; }
    public AcquisitionType Acquisition { get; set; }
    public decimal? Price { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}

public enum AvailabilityStatus { Available, Doubtful, Injured, Unavailable, NationalDuty, Personal }

/// <summary>Public availability summary. Detailed medical data lives in PlayerMedicalRecord.</summary>
public class PlayerAvailability : Entity
{
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public AvailabilityStatus Status { get; set; }
    /// <summary>Non-medical reason that every franchise user may see.</summary>
    public string? PublicNote { get; set; }
    public DateOnly? ExpectedReturn { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
