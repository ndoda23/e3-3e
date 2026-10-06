using Lams.Domain.Common;

namespace Lams.Domain.Users;

/// <summary>
/// Guard rails that keep the system administrable:
/// an administrator cannot deactivate or demote themselves, and at least one active administrator must remain.
/// </summary>
public static class AdminContinuityPolicy
{
    /// <param name="actor">The administrator performing the change.</param>
    /// <param name="target">The user being deactivated.</param>
    /// <param name="targetIsActiveAdministrator">Whether the target is currently an active administrator.</param>
    /// <param name="activeAdministratorCount">Number of active administrators before the change.</param>
    public static Result EnsureCanDeactivate(
        UserId actor, UserId target, bool targetIsActiveAdministrator, int activeAdministratorCount)
    {
        if (actor == target)
        {
            return Result.Failure(UserErrors.CannotDeactivateSelf);
        }

        return targetIsActiveAdministrator && activeAdministratorCount <= 1
            ? Result.Failure(UserErrors.LastActiveAdministrator)
            : Result.Success();
    }

    /// <param name="actor">The administrator performing the change.</param>
    /// <param name="target">The user whose role changes.</param>
    /// <param name="targetIsActiveAdministrator">Whether the target is currently an active administrator.</param>
    /// <param name="newRoleIsAdministrative">Whether the new role is an administrative role.</param>
    /// <param name="activeAdministratorCount">Number of active administrators before the change.</param>
    public static Result EnsureCanChangeRole(
        UserId actor,
        UserId target,
        bool targetIsActiveAdministrator,
        bool newRoleIsAdministrative,
        int activeAdministratorCount)
    {
        var isDemotion = targetIsActiveAdministrator && !newRoleIsAdministrative;
        if (!isDemotion)
        {
            return Result.Success();
        }

        if (actor == target)
        {
            return Result.Failure(UserErrors.CannotDemoteSelf);
        }

        return activeAdministratorCount <= 1
            ? Result.Failure(UserErrors.LastActiveAdministrator)
            : Result.Success();
    }
}
