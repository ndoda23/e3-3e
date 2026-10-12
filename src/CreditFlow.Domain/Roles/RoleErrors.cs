using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Roles;

public static class RoleErrors
{
    public static readonly Error InvalidCode = Error.Validation(
        "Role.InvalidCode", "A role code has 2 to 16 uppercase letters or digits.", "Code");

    public static readonly Error SystemRoleCannotBeDeleted = Error.Conflict(
        "Role.SystemRoleCannotBeDeleted", "System roles cannot be deleted.");

    public static readonly Error SystemRoleCannotBeRenamed = Error.Conflict(
        "Role.SystemRoleCannotBeRenamed", "System roles cannot be renamed.");

    public static Error InvalidPermission(string code) => Error.Validation(
        "Role.InvalidPermission", $"'{code}' is not a valid permission code.", "Permissions");

    public static Error BusinessRoleCannotHoldAdministrativePermission(PermissionCode permission) => Error.Validation(
        "Role.BusinessRoleCannotHoldAdministrativePermission",
        $"A business role cannot hold the administration permission '{permission}'.",
        "Permissions");

    public static Error ReadOnlyRoleCannotHoldWritePermission(PermissionCode permission) => Error.Validation(
        "Role.ReadOnlyRoleCannotHoldWritePermission",
        $"A read-only role can hold only read permissions, not '{permission}'.",
        "Permissions");

    public static Error AdministrativeRoleCannotHoldBusinessPermission(PermissionCode permission) => Error.Validation(
        "Role.AdministrativeRoleCannotHoldBusinessPermission",
        $"An administrative role can hold only administration permissions, not '{permission}'.",
        "Permissions");
}
