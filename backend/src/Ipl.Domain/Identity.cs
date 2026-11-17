namespace Ipl.Domain;

public class User : AuditableEntity
{
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>False until an invited user sets a password.</summary>
    public bool IsActivated { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndsAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    /// <summary>Changes on password reset/deactivation; access tokens with an old stamp are rejected.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public Guid? DefaultFranchiseId { get; set; }

    public List<UserRole> Roles { get; set; } = [];
}

public enum RoleScope { Platform, League, Franchise }

public class Role
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public RoleScope Scope { get; set; }
    public int SortOrder { get; set; }
    public List<RolePermission> Permissions { get; set; } = [];
}

public class Permission
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Module { get; set; } = "";
    public string Description { get; set; } = "";
}

public class RolePermission
{
    public int RoleId { get; set; }
    public Role? Role { get; set; }
    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }
}

/// <summary>A role held by a user; franchise roles carry the franchise they apply to.</summary>
public class UserRole : Entity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public int RoleId { get; set; }
    public Role? Role { get; set; }
    public Guid? FranchiseId { get; set; }
    public Franchise? Franchise { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid? AssignedBy { get; set; }
}

/// <summary>Refresh tokens are stored hashed and rotated on every use; reuse revokes the whole family.</summary>
public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = "";
    public Guid FamilyId { get; set; }
    public Guid? FranchiseId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedById { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}

public class LoginHistory : Entity
{
    public Guid? UserId { get; set; }
    public string Email { get; set; } = "";
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime At { get; set; }
}

public enum UserTokenPurpose { PasswordReset, Invitation }

/// <summary>One-time tokens (password reset, invitation). Only a hash is stored.</summary>
public class UserToken : Entity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public UserTokenPurpose Purpose { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}

/// <summary>Outgoing e-mails. Sent by a background service when SMTP is configured.</summary>
public class EmailMessage : Entity
{
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public Guid? FranchiseId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
}
