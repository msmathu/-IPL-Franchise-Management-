namespace Ipl.Domain;

/// <summary>Base for all entities with a GUID key (time-ordered v7 GUIDs keep clustered indexes compact).</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>Created/updated stamps, filled in automatically when changes are saved.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    Guid? UpdatedBy { get; set; }
}

/// <summary>Rows that are hidden instead of physically deleted.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

/// <summary>Confidential data that belongs to exactly one franchise (tenant).</summary>
public interface IFranchiseOwned
{
    Guid FranchiseId { get; }
}

public abstract class AuditableEntity : Entity, IAuditable
{
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>A business rule was violated; mapped to HTTP 400/409/422 by the API.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Money helpers. All amounts are stored in Indian rupees (decimal 18,2).</summary>
public static class Money
{
    public const decimal Lakh = 100_000m;
    public const decimal Crore = 10_000_000m;

    public static string Format(decimal rupees) =>
        Math.Abs(rupees) >= Crore ? $"₹{rupees / Crore:0.##} Cr" : $"₹{rupees / Lakh:0.##} L";
}
