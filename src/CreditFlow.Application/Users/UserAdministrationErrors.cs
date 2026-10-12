using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Users;

public static class UserAdministrationErrors
{
    public static readonly Error UserNameTaken = Error.Conflict(
        "User.UserNameTaken", "Another user already has this user name.", "UserName");

    public static readonly Error EmailTaken = Error.Conflict(
        "User.EmailTaken", "Another user already has this email address.", "Email");

    public static readonly Error RoleNotFound = Error.Validation(
        "User.RoleNotFound", "The selected role does not exist.", "RoleId");

    public static readonly Error BranchNotFound = Error.Validation(
        "User.BranchNotFound", "The selected branch does not exist.", "BranchId");
}
