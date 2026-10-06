using Lams.Domain.Common;

namespace Lams.Domain.Branches;

public static class BranchErrors
{
    public static readonly Error InvalidCode = Error.Validation(
        "Branch.InvalidCode", "A branch code has 2 to 10 uppercase letters, digits or hyphens.", "Code");

    public static readonly Error InvalidName = Error.Validation(
        "Branch.InvalidName", "A branch name is required (up to 100 characters).", "Name");

    public static readonly Error InvalidCity = Error.Validation(
        "Branch.InvalidCity", "A city is required (up to 100 characters).", "City");

    public static readonly Error DuplicateCode = Error.Conflict(
        "Branch.DuplicateCode", "Another branch already uses this code.", "Code");

    public static readonly Error HasAssignedUsers = Error.Conflict(
        "Branch.HasAssignedUsers", "Users are assigned to this branch. Deactivate it instead of deleting it.");

    public static readonly Error NotFound = Error.NotFound(
        "Branch.NotFound", "The branch was not found.");

    public static readonly Error Inactive = Error.Validation(
        "Branch.Inactive", "The selected branch is inactive.", "BranchId");
}
