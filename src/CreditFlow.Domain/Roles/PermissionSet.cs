using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Roles;

/// <summary>
/// The permissions granted to one role. Enforces segregation of duties by <see cref="RoleKind"/>:
/// an administrative role holds only <c>admin.*</c> permissions, a business role never holds one, and a read-only
/// role holds only non-administrative permissions that are read-only.
/// </summary>
public sealed class PermissionSet
{
    private readonly HashSet<PermissionCode> _permissions;

    private PermissionSet(HashSet<PermissionCode> permissions) => _permissions = permissions;

    public IReadOnlyCollection<PermissionCode> Permissions => _permissions;

    public int Count => _permissions.Count;

    /// <param name="kind">The role's kind.</param>
    /// <param name="permissionCodes">The permissions to grant.</param>
    /// <param name="isReadOnly">Whether a permission only reads data (the permission catalog knows this).</param>
    public static Result<PermissionSet> Create(
        RoleKind kind, IEnumerable<string> permissionCodes, Func<PermissionCode, bool> isReadOnly)
    {
        ArgumentNullException.ThrowIfNull(permissionCodes);
        ArgumentNullException.ThrowIfNull(isReadOnly);

        var permissions = new HashSet<PermissionCode>();
        foreach (var code in permissionCodes)
        {
            var permission = PermissionCode.Create(code);
            if (permission.IsFailure)
            {
                return Result.Failure<PermissionSet>(permission.Error!);
            }

            var violation = CheckKind(kind, permission.Value, isReadOnly);
            if (violation is not null)
            {
                return Result.Failure<PermissionSet>(violation);
            }

            permissions.Add(permission.Value);
        }

        return Result.Success(new PermissionSet(permissions));
    }

    public bool Contains(string permissionCode) =>
        PermissionCode.Create(permissionCode) is { IsSuccess: true } permission && _permissions.Contains(permission.Value);

    private static Error? CheckKind(RoleKind kind, PermissionCode permission, Func<PermissionCode, bool> isReadOnly) =>
        kind switch
        {
            RoleKind.Administrative when !permission.IsAdministrative =>
                RoleErrors.AdministrativeRoleCannotHoldBusinessPermission(permission),
            RoleKind.Business or RoleKind.ReadOnly when permission.IsAdministrative =>
                RoleErrors.BusinessRoleCannotHoldAdministrativePermission(permission),
            RoleKind.ReadOnly when !isReadOnly(permission) =>
                RoleErrors.ReadOnlyRoleCannotHoldWritePermission(permission),
            RoleKind.Administrative or RoleKind.Business or RoleKind.ReadOnly => null,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown role kind."),
        };
}
