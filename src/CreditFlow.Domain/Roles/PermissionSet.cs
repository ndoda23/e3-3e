using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Roles;

/// <summary>
/// The permissions granted to one role. Enforces segregation of duties: an administrative role may hold
/// only <c>admin.*</c> permissions, and a business role may never hold an <c>admin.*</c> permission.
/// </summary>
public sealed class PermissionSet
{
    private readonly HashSet<PermissionCode> _permissions;

    private PermissionSet(HashSet<PermissionCode> permissions) => _permissions = permissions;

    public IReadOnlyCollection<PermissionCode> Permissions => _permissions;

    public int Count => _permissions.Count;

    public static Result<PermissionSet> Create(bool roleIsAdministrative, IEnumerable<string> permissionCodes)
    {
        ArgumentNullException.ThrowIfNull(permissionCodes);

        var permissions = new HashSet<PermissionCode>();
        foreach (var code in permissionCodes)
        {
            var permission = PermissionCode.Create(code);
            if (permission.IsFailure)
            {
                return Result.Failure<PermissionSet>(permission.Error!);
            }

            if (permission.Value.IsAdministrative != roleIsAdministrative)
            {
                return Result.Failure<PermissionSet>(roleIsAdministrative
                    ? RoleErrors.AdministrativeRoleCannotHoldBusinessPermission(permission.Value)
                    : RoleErrors.BusinessRoleCannotHoldAdministrativePermission(permission.Value));
            }

            permissions.Add(permission.Value);
        }

        return Result.Success(new PermissionSet(permissions));
    }

    public bool Contains(string permissionCode) =>
        PermissionCode.Create(permissionCode) is { IsSuccess: true } permission && _permissions.Contains(permission.Value);
}
