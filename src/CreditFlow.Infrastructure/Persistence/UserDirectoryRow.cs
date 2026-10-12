using CreditFlow.Domain.Users;

namespace CreditFlow.Infrastructure.Persistence;

/// <summary>
/// A row of the <c>vw_UserDirectory</c> view: staff member + account + role + branch with plain column types,
/// used for the user list, user details and administrator counts (read-only).
/// </summary>
public sealed class UserDirectoryRow
{
    public const string ViewName = "vw_UserDirectory";

    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public Guid RoleId { get; set; }

    public string RoleCode { get; set; } = string.Empty;

    public string RoleName { get; set; } = string.Empty;

    public bool RoleIsAdministrative { get; set; }

    public bool RoleRequiresMfa { get; set; }

    public Guid? BranchId { get; set; }

    public string? BranchName { get; set; }

    public Segment? Segment { get; set; }

    public AuthorityLevel? AuthorityLevel { get; set; }

    public bool IsActive { get; set; }

    public bool IsAvailable { get; set; }

    public int Capacity { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public bool TwoFactorEnabled { get; set; }

    public bool RequireMfa { get; set; }

    public bool MustChangePassword { get; set; }

    public DateTimeOffset? TemporaryPasswordExpiresAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
