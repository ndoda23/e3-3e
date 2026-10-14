using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Roles;

public static class RoleAdministrationErrors
{
    public static readonly Error NotFound = Error.NotFound("Role.NotFound", "The role was not found.");

    public static Error UnknownPermission(string code) => Error.Validation(
        "Role.UnknownPermission", $"'{code}' is not a known permission.", "Permissions");

    public static Error MustKeepAdministrationPermission(string code) => Error.Validation(
        "Role.MustKeepAdministrationPermission",
        $"The administrator role must keep the permission '{code}', otherwise nobody could administer the system.",
        "Permissions");
}
