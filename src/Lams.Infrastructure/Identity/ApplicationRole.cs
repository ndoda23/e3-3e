using Lams.Domain.Roles;
using Microsoft.AspNetCore.Identity;

namespace Lams.Infrastructure.Identity;

/// <summary>
/// A role with its access-relevant definition stored as data. <see cref="IdentityRole{TKey}.Name"/> equals
/// <see cref="Code"/> (Identity requires a unique name); code never checks either: it checks permissions.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    public const int DisplayNameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DataScope DataScope { get; set; }

    public DashboardKind Dashboard { get; set; }

    public AttributeRule BranchRule { get; set; }

    public AttributeRule SegmentRule { get; set; }

    public AttributeRule AuthorityLevelRule { get; set; }

    public bool IsAdministrative { get; set; }

    /// <summary>Users of this role must enrol in MFA.</summary>
    public bool RequireMfa { get; set; }

    /// <summary>System roles cannot be deleted or renamed.</summary>
    public bool IsSystem { get; set; }

    public List<RolePermission> Permissions { get; } = [];

    public RoleProfile ToProfile() => new()
    {
        Id = new RoleId(Id),
        Code = RoleCode.Create(Code).Value,
        DataScope = DataScope,
        Dashboard = Dashboard,
        BranchRule = BranchRule,
        SegmentRule = SegmentRule,
        AuthorityLevelRule = AuthorityLevelRule,
        IsAdministrative = IsAdministrative,
    };
}
