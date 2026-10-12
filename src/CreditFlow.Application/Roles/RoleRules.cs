using CreditFlow.Application.Authorization;

namespace CreditFlow.Application.Roles;

public static class RoleRules
{
    /// <summary>
    /// Permissions an administrative role must always keep, so that administration can never lock itself out.
    /// </summary>
    public static IReadOnlyList<string> RequiredAdministrativePermissions { get; } =
        [Permissions.Admin.UsersManage, Permissions.Admin.RolesManage];
}
