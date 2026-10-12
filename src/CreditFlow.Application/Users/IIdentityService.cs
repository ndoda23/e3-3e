using CreditFlow.Domain.Common;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.Users;

/// <summary>Data for a new authentication account. Its id is the id of the matching <see cref="StaffMember"/>.</summary>
public sealed record NewAccount(
    UserId Id,
    string UserName,
    string Email,
    string TemporaryPassword,
    DateTimeOffset TemporaryPasswordExpiresAt,
    bool RequireMfa);

/// <summary>Authentication data of a user account (no business data: that lives on <see cref="StaffMember"/>).</summary>
public sealed record AccountInfo(
    UserId Id,
    string UserName,
    string Email,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd,
    bool TwoFactorEnabled,
    bool RequireMfa,
    bool MustChangePassword,
    DateTimeOffset? TemporaryPasswordExpiresAt,
    DateTimeOffset? LastLoginAt);

/// <summary>
/// Port over the authentication account (ASP.NET Core Identity): credentials, MFA, lockout and the security stamp.
/// Writes join the ambient transaction started by <c>IUnitOfWork.ExecuteInTransactionAsync</c>.
/// </summary>
public interface IIdentityService
{
    /// <summary>Creates the account with a temporary password that must be changed at first sign-in.</summary>
    Task<Result> CreateAccountAsync(NewAccount account, CancellationToken cancellationToken);

    Task<AccountInfo?> FindAccountAsync(UserId id, CancellationToken cancellationToken);

    /// <param name="userName">The user name to check.</param>
    /// <param name="exceptUserId">An account to ignore (the one being edited).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsUserNameTakenAsync(string userName, UserId? exceptUserId, CancellationToken cancellationToken);

    /// <param name="email">The email to check.</param>
    /// <param name="exceptUserId">An account to ignore (the one being edited).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsEmailTakenAsync(string email, UserId? exceptUserId, CancellationToken cancellationToken);

    Task<Result> UpdateAccountAsync(UserId id, string userName, string email, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the password with a temporary one (must be changed at next sign-in, expires at
    /// <paramref name="expiresAt"/>) and ends all existing sessions.
    /// </summary>
    Task<Result> SetTemporaryPasswordAsync(
        UserId id, string temporaryPassword, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Ends the lockout and resets the failed-attempt counter.</summary>
    Task<Result> UnlockAsync(UserId id, CancellationToken cancellationToken);

    /// <summary>Requires (or stops requiring) MFA enrolment for this user.</summary>
    Task<Result> SetRequireMfaAsync(UserId id, bool requireMfa, CancellationToken cancellationToken);

    /// <summary>Rotates the security stamp so that existing sessions end at the next validation.</summary>
    Task<Result> InvalidateSessionsAsync(UserId id, CancellationToken cancellationToken);
}

/// <summary>Generates one-time temporary passwords that satisfy the password policy.</summary>
public interface ITemporaryPasswordGenerator
{
    string Generate();
}
