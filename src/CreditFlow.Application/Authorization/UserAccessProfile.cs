using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.Authorization;

/// <summary>
/// Everything authorization needs to know about a user, built from the <see cref="StaffMember"/> and their role.
/// Resolved per request (cached); never taken from cookie claims. An inactive user holds no permissions.
/// </summary>
public sealed record UserAccessProfile
{
    public required UserId UserId { get; init; }

    public required string FullName { get; init; }

    public required RoleId RoleId { get; init; }

    public required string RoleCode { get; init; }

    public required string RoleName { get; init; }

    public required bool IsAdministrative { get; init; }

    public required DataScope DataScope { get; init; }

    public required DashboardKind Dashboard { get; init; }

    public BranchId? BranchId { get; init; }

    public string? BranchName { get; init; }

    public required bool IsActive { get; init; }

    public required IReadOnlySet<string> Permissions { get; init; }

    public bool HasPermission(string permission) => IsActive && Permissions.Contains(permission);

    public bool HasAnyPermission(params ReadOnlySpan<string> permissions)
    {
        foreach (var permission in permissions)
        {
            if (HasPermission(permission))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the user holds at least one <c>admin.*</c> permission.</summary>
    public bool HasAnyAdministrativePermission =>
        IsActive && Permissions.Any(p => p.StartsWith(PermissionCode.AdministrativePrefix, StringComparison.Ordinal));

    /// <param name="staff">The staff member.</param>
    /// <param name="role">The staff member's current role.</param>
    /// <param name="roleName">Display name of the role.</param>
    /// <param name="permissions">Permissions granted to the role.</param>
    /// <param name="branchName">Display name of the staff member's branch, if any.</param>
    public static UserAccessProfile Create(
        StaffMember staff,
        RoleProfile role,
        string roleName,
        IEnumerable<string> permissions,
        string? branchName = null)
    {
        ArgumentNullException.ThrowIfNull(staff);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(permissions);

        if (staff.RoleId != role.Id)
        {
            throw new ArgumentException($"Role {role.Id} is not the role of staff member {staff.Id}.", nameof(role));
        }

        return FromParts(
            staff.Id,
            staff.FullName,
            role,
            roleName,
            permissions,
            staff.BranchId,
            branchName,
            staff.IsActive);
    }

    /// <summary>Builds a profile from cached parts (staff snapshot + role), applying the same fail-closed rules.</summary>
    public static UserAccessProfile FromParts(
        UserId userId,
        string fullName,
        RoleProfile role,
        string roleName,
        IEnumerable<string> permissions,
        BranchId? branchId,
        string? branchName,
        bool isActive)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(permissions);

        return new UserAccessProfile
        {
            UserId = userId,
            FullName = fullName,
            RoleId = role.Id,
            RoleCode = role.Code.Value,
            RoleName = roleName,
            IsAdministrative = role.IsAdministrative,
            DataScope = role.DataScope,
            Dashboard = role.Dashboard,
            BranchId = branchId,
            BranchName = branchId is null ? null : branchName,
            IsActive = isActive,

            // Fail closed: an inactive user carries no permissions at all.
            Permissions = isActive
                ? permissions.ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal),
        };
    }
}
