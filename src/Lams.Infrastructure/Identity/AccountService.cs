using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Lams.Application.Auditing;
using Lams.Application.Authorization;
using Lams.Domain.Auditing;
using Lams.Domain.Common;
using Lams.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Lams.Infrastructure.Identity;

public enum SignInStatus
{
    Succeeded = 1,
    RequiresTwoFactor = 2,
    LockedOut = 3,
    Failed = 4,
    TemporaryPasswordExpired = 5,
}

/// <param name="SharedKey">The authenticator key, formatted in groups of four for manual entry.</param>
/// <param name="AuthenticatorUri">The <c>otpauth://</c> URI encoded in the QR code.</param>
public sealed record AuthenticatorSetup(string SharedKey, string AuthenticatorUri);

/// <param name="Enabled">Two-factor authentication is enabled.</param>
/// <param name="Required">The role or the account requires MFA (it cannot be turned off).</param>
/// <param name="RecoveryCodesLeft">Unused recovery codes.</param>
public sealed record TwoFactorStatus(bool Enabled, bool Required, int RecoveryCodesLeft);

/// <summary>
/// Sign-in and self-service account operations on top of ASP.NET Core Identity, with an audit record for every
/// security event. Business data (role, branch, …) is never read from or written to the account here.
/// </summary>
public sealed class AccountService(
    LamsSignInManager signInManager,
    UserManager<ApplicationUser> userManager,
    IUserAccessProfileService accessProfiles,
    IAccessCacheInvalidator accessCache,
    IAuditWriter audit,
    TimeProvider clock)
{
    public const string Issuer = "LAMS";
    public const int RecoveryCodeCount = 10;
    private const string UserEntity = "User";

    /// <summary>
    /// Verifies user name or email and password. Lockout (5 failures → 15 minutes) is applied by Identity; a locked
    /// account is reported as locked before the password is checked. An expired temporary password fails only after
    /// the password was verified, so its message does not reveal whether an account exists.
    /// </summary>
    public async Task<SignInStatus> PasswordSignInAsync(string login, string password, CancellationToken cancellationToken)
    {
        var user = await FindByLoginAsync(login);
        if (user is null)
        {
            await AuditAsync(AuditActions.LoginFailed, null, login, cancellationToken, reason: "UnknownUser");
            return SignInStatus.Failed;
        }

        var check = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            await AuditAsync(AuditActions.LockedOut, user, null, cancellationToken);
            return SignInStatus.LockedOut;
        }

        if (check.IsNotAllowed)
        {
            await AuditAsync(AuditActions.LoginFailed, user, null, cancellationToken, reason: "NotAllowed");
            return SignInStatus.Failed;
        }

        if (!check.Succeeded)
        {
            await AuditAsync(AuditActions.LoginFailed, user, null, cancellationToken, reason: "InvalidPassword");
            return SignInStatus.Failed;
        }

        if (TemporaryPasswordPolicy.IsExpired(user.TemporaryPasswordExpiresAt, clock.GetUtcNow()))
        {
            await AuditAsync(AuditActions.LoginFailedTemporaryPasswordExpired, user, null, cancellationToken);
            return SignInStatus.TemporaryPasswordExpired;
        }

        var result = await signInManager.SignInAfterPasswordCheckAsync(user);
        if (result.RequiresTwoFactor)
        {
            return SignInStatus.RequiresTwoFactor;
        }

        if (!result.Succeeded)
        {
            await AuditAsync(AuditActions.LoginFailed, user, null, cancellationToken, reason: "NotAllowed");
            return SignInStatus.Failed;
        }

        await CompleteSignInAsync(user, cancellationToken);
        return SignInStatus.Succeeded;
    }

    /// <summary>Second step with a code from the authenticator app.</summary>
    public Task<SignInStatus> TwoFactorSignInAsync(string code, CancellationToken cancellationToken) =>
        SecondFactorAsync(
            () => signInManager.TwoFactorAuthenticatorSignInAsync(NormalizeCode(code), isPersistent: false, rememberClient: false),
            "InvalidAuthenticatorCode",
            cancellationToken);

    /// <summary>Second step with a one-time recovery code.</summary>
    public Task<SignInStatus> RecoveryCodeSignInAsync(string recoveryCode, CancellationToken cancellationToken) =>
        SecondFactorAsync(
            () => signInManager.TwoFactorRecoveryCodeSignInAsync(NormalizeCode(recoveryCode)),
            "InvalidRecoveryCode",
            cancellationToken);

    /// <summary>True while a password-verified user is between the password and the two-factor step.</summary>
    public async Task<bool> IsTwoFactorPendingAsync() => await signInManager.GetTwoFactorAuthenticationUserAsync() is not null;

    public async Task SignOutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated == true && await userManager.GetUserAsync(principal) is { } user)
        {
            await AuditAsync(AuditActions.Logout, user, null, cancellationToken);
        }

        await signInManager.SignOutAsync();
    }

    /// <summary>
    /// Changes the password (current password required; the last 5 cannot be reused), clears the temporary-password
    /// state, and re-issues the cookie with the new security stamp (the original sign-in time is kept).
    /// </summary>
    public async Task<Result> ChangePasswordAsync(
        ClaimsPrincipal principal, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Result.Failure(AccountErrors.NotSignedIn);
        }

        var changed = (await userManager.ChangePasswordAsync(user, currentPassword, newPassword)).ToResult();
        if (changed.IsFailure)
        {
            return changed;
        }

        user.MustChangePassword = false;
        user.TemporaryPasswordExpiresAt = null;
        var updated = (await userManager.UpdateAsync(user)).ToResult();
        if (updated.IsFailure)
        {
            return updated;
        }

        await signInManager.RefreshSignInAsync(user);
        await AuditAsync(AuditActions.PasswordChanged, user, null, cancellationToken);
        await accessCache.InvalidateUserAsync(new UserId(user.Id), cancellationToken);
        return Result.Success();
    }

    public async Task<TwoFactorStatus?> GetTwoFactorStatusAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return null;
        }

        var state = await accessProfiles.GetSecurityStateAsync(new UserId(user.Id), cancellationToken);
        return new TwoFactorStatus(
            await userManager.GetTwoFactorEnabledAsync(user),
            state?.MfaRequired ?? user.RequireMfa,
            await userManager.CountRecoveryCodesAsync(user));
    }

    /// <summary>The key and QR-code URI for setting up an authenticator app (creates a key if the user has none).</summary>
    public async Task<AuthenticatorSetup?> GetAuthenticatorSetupAsync(ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return null;
        }

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        var account = Uri.EscapeDataString(user.UserName ?? user.Email ?? user.Id.ToString());
        var uri = string.Create(
            CultureInfo.InvariantCulture,
            $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{account}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6");
        return new AuthenticatorSetup(FormatKey(key!), uri);
    }

    /// <summary>Verifies a code from the app, enables MFA and returns new recovery codes (shown once).</summary>
    public async Task<Result<IReadOnlyList<string>>> EnableAuthenticatorAsync(
        ClaimsPrincipal principal, string code, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Result.Failure<IReadOnlyList<string>>(AccountErrors.NotSignedIn);
        }

        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user, userManager.Options.Tokens.AuthenticatorTokenProvider, NormalizeCode(code));
        if (!valid)
        {
            return Result.Failure<IReadOnlyList<string>>(AccountErrors.InvalidAuthenticatorCode);
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))?.ToList() ?? [];

        await signInManager.RefreshSignInAsync(user);
        await AuditAsync(AuditActions.MfaEnabled, user, null, cancellationToken);
        await accessCache.InvalidateUserAsync(new UserId(user.Id), cancellationToken);
        return Result.Success<IReadOnlyList<string>>(codes);
    }

    /// <summary>Turns MFA off, unless the role or the account requires it.</summary>
    public async Task<Result> DisableTwoFactorAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Result.Failure(AccountErrors.NotSignedIn);
        }

        var state = await accessProfiles.GetSecurityStateAsync(new UserId(user.Id), cancellationToken);
        if (state?.MfaRequired != false || user.RequireMfa)
        {
            return Result.Failure(AccountErrors.MfaRequired);
        }

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await signInManager.RefreshSignInAsync(user);
        await AuditAsync(AuditActions.MfaDisabled, user, null, cancellationToken);
        await accessCache.InvalidateUserAsync(new UserId(user.Id), cancellationToken);
        return Result.Success();
    }

    /// <summary>Replaces the authenticator key and turns MFA off until the user sets up the app again.</summary>
    public async Task<Result> ResetAuthenticatorAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Result.Failure(AccountErrors.NotSignedIn);
        }

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await signInManager.RefreshSignInAsync(user);
        await AuditAsync(AuditActions.MfaReset, user, null, cancellationToken);
        await accessCache.InvalidateUserAsync(new UserId(user.Id), cancellationToken);
        return Result.Success();
    }

    /// <summary>Replaces all recovery codes; the new ones are shown once.</summary>
    public async Task<Result<IReadOnlyList<string>>> GenerateRecoveryCodesAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Result.Failure<IReadOnlyList<string>>(AccountErrors.NotSignedIn);
        }

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            return Result.Failure<IReadOnlyList<string>>(AccountErrors.MfaNotEnabled);
        }

        var codes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))?.ToList() ?? [];
        await AuditAsync(AuditActions.RecoveryCodesGenerated, user, null, cancellationToken);
        return Result.Success<IReadOnlyList<string>>(codes);
    }

    private async Task<SignInStatus> SecondFactorAsync(
        Func<Task<SignInResult>> signIn, string failureReason, CancellationToken cancellationToken)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return SignInStatus.Failed;
        }

        var result = await signIn();
        if (result.Succeeded)
        {
            await CompleteSignInAsync(user, cancellationToken);
            return SignInStatus.Succeeded;
        }

        if (result.IsLockedOut)
        {
            await AuditAsync(AuditActions.LockedOut, user, null, cancellationToken);
            return SignInStatus.LockedOut;
        }

        await AuditAsync(AuditActions.LoginFailed, user, null, cancellationToken, reason: failureReason);
        return SignInStatus.Failed;
    }

    private async Task CompleteSignInAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.LastLoginAt = clock.GetUtcNow();
        await userManager.UpdateAsync(user);
        await AuditAsync(AuditActions.LoginSucceeded, user, null, cancellationToken);
    }

    private async Task<ApplicationUser?> FindByLoginAsync(string login)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        var trimmed = login.Trim();
        return trimmed.Contains('@', StringComparison.Ordinal)
            ? await userManager.FindByEmailAsync(trimmed) ?? await userManager.FindByNameAsync(trimmed)
            : await userManager.FindByNameAsync(trimmed);
    }

    private Task AuditAsync(
        string action, ApplicationUser? user, string? attemptedLogin, CancellationToken cancellationToken, string? reason = null) =>
        audit.WriteAsync(
            new AuditEvent(action)
            {
                UserId = user?.Id,
                UserName = user?.UserName ?? attemptedLogin?.Trim(),
                EntityType = UserEntity,
                EntityId = user?.Id.ToString(),
                AfterJson = reason is null ? null : JsonSerializer.Serialize(new { reason }),
            },
            cancellationToken);

    private static string NormalizeCode(string code) =>
        (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);

    private static string FormatKey(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return builder.ToString().ToLowerInvariant();
    }
}
