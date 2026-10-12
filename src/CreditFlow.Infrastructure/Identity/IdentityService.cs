using CreditFlow.Application.Users;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace CreditFlow.Infrastructure.Identity;

/// <summary><see cref="IIdentityService"/> over ASP.NET Core Identity's <see cref="UserManager{TUser}"/>.</summary>
internal sealed class IdentityService(UserManager<ApplicationUser> userManager, TimeProvider clock) : IIdentityService
{
    public async Task<Result> CreateAccountAsync(NewAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var user = new ApplicationUser
        {
            Id = account.Id.Value,
            UserName = account.UserName,
            Email = account.Email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            MustChangePassword = true,
            TemporaryPasswordExpiresAt = account.TemporaryPasswordExpiresAt,
            RequireMfa = account.RequireMfa,
        };

        return (await userManager.CreateAsync(user, account.TemporaryPassword)).ToResult();
    }

    public async Task<AccountInfo?> FindAccountAsync(UserId id, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        return user is null
            ? null
            : new AccountInfo(
                id,
                user.UserName ?? string.Empty,
                user.Email ?? string.Empty,
                user.LockoutEnd > clock.GetUtcNow(),
                user.LockoutEnd,
                user.TwoFactorEnabled,
                user.RequireMfa,
                user.MustChangePassword,
                user.TemporaryPasswordExpiresAt,
                user.LastLoginAt);
    }

    public async Task<bool> IsUserNameTakenAsync(string userName, UserId? exceptUserId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(userName);
        return user is not null && user.Id != exceptUserId?.Value;
    }

    public async Task<bool> IsEmailTakenAsync(string email, UserId? exceptUserId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user is not null && user.Id != exceptUserId?.Value;
    }

    public async Task<Result> UpdateAccountAsync(
        UserId id, string userName, string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Result.Failure(IdentityErrors.AccountNotFound);
        }

        if (!string.Equals(user.UserName, userName, StringComparison.Ordinal))
        {
            var renamed = (await userManager.SetUserNameAsync(user, userName)).ToResult();
            if (renamed.IsFailure)
            {
                return renamed;
            }
        }

        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var changed = (await userManager.SetEmailAsync(user, email)).ToResult();
            if (changed.IsFailure)
            {
                return changed;
            }

            // Email addresses are set by administrators; there is no confirmation flow.
            user.EmailConfirmed = true;
            return (await userManager.UpdateAsync(user)).ToResult();
        }

        return Result.Success();
    }

    public async Task<Result> SetTemporaryPasswordAsync(
        UserId id, string temporaryPassword, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Result.Failure(IdentityErrors.AccountNotFound);
        }

        // ResetPasswordAsync runs the password validators and rotates the security stamp (ends all sessions).
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = (await userManager.ResetPasswordAsync(user, token, temporaryPassword)).ToResult();
        if (reset.IsFailure)
        {
            return reset;
        }

        user.MustChangePassword = true;
        user.TemporaryPasswordExpiresAt = expiresAt;
        return (await userManager.UpdateAsync(user)).ToResult();
    }

    public async Task<Result> UnlockAsync(UserId id, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Result.Failure(IdentityErrors.AccountNotFound);
        }

        var unlocked = (await userManager.SetLockoutEndDateAsync(user, null)).ToResult();
        return unlocked.IsFailure ? unlocked : (await userManager.ResetAccessFailedCountAsync(user)).ToResult();
    }

    public async Task<Result> SetRequireMfaAsync(UserId id, bool requireMfa, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Result.Failure(IdentityErrors.AccountNotFound);
        }

        user.RequireMfa = requireMfa;
        return (await userManager.UpdateAsync(user)).ToResult();
    }

    public async Task<Result> InvalidateSessionsAsync(UserId id, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        return user is null
            ? Result.Failure(IdentityErrors.AccountNotFound)
            : (await userManager.UpdateSecurityStampAsync(user)).ToResult();
    }
}
