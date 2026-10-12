using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Users;

public static class UserErrors
{
    public static readonly Error RoleRequired = Error.Validation(
        "User.RoleRequired", "A user must have a role.", "RoleId");

    public static readonly Error NotFound = Error.NotFound(
        "User.NotFound", "The user was not found.");

    public static readonly Error InvalidFullName = Error.Validation(
        "User.InvalidFullName", "A full name is required (up to 200 characters).", "FullName");

    public static readonly Error InvalidCapacity = Error.Validation(
        "User.InvalidCapacity", "Capacity must be between 0 and 100.", "Capacity");

    public static readonly Error InactiveCannotBeAvailable = Error.Conflict(
        "User.InactiveCannotBeAvailable", "An inactive user cannot be marked as working.");

    public static readonly Error BranchRequired = Error.Validation(
        "User.BranchRequired", "The selected role requires a branch.", "BranchId");
    public static readonly Error CannotDeactivateSelf = Error.Forbidden(
        "User.CannotDeactivateSelf", "You cannot deactivate your own account.");

    public static readonly Error CannotDemoteSelf = Error.Forbidden(
        "User.CannotDemoteSelf", "You cannot remove your own administrator role.");

    public static readonly Error LastActiveAdministrator = Error.Conflict(
        "User.LastActiveAdministrator", "At least one active System Administrator must remain.");
}
