using Lams.Domain.Roles;
using Lams.Domain.Users;

namespace Lams.Application.Authorization;

/// <summary>
/// What a signed-in user must do before using the application: change a temporary password, or enrol in MFA when
/// their role or their account requires it.
/// </summary>
/// <param name="MustChangePassword">The user signed in with a temporary password.</param>
/// <param name="MfaRequired">MFA is required by the role or by the account.</param>
/// <param name="MfaEnrolled">An authenticator app is set up (two-factor enabled).</param>
public sealed record AccountSecurityState(bool MustChangePassword, bool MfaRequired, bool MfaEnrolled)
{
    /// <summary>The user still has to enrol in MFA.</summary>
    public bool MustEnrolMfa => MfaRequired && !MfaEnrolled;
}

/// <summary>Resolves a user's access profile (cached; invalidated through <see cref="IAccessCacheInvalidator"/>).</summary>
public interface IUserAccessProfileService
{
    /// <summary>The profile, or <c>null</c> when the user has no staff record.</summary>
    Task<UserAccessProfile?> GetAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>The account set-up state (cached with the profile), or <c>null</c> when the user has no staff record.</summary>
    Task<AccountSecurityState?> GetSecurityStateAsync(UserId userId, CancellationToken cancellationToken);
}

/// <summary>Removes cached access profiles after changes that affect them.</summary>
public interface IAccessCacheInvalidator
{
    /// <summary>
    /// After a change to one user's role, active flag, branch, segment or authority level, or to their account set-up
    /// state (password changed or reset, MFA enrolled, disabled or required).
    /// </summary>
    Task InvalidateUserAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>After a change to a role's permissions or settings (affects every user of the role).</summary>
    Task InvalidateRoleAsync(RoleId roleId, CancellationToken cancellationToken);
}
