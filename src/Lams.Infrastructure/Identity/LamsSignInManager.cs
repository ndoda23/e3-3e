using System.Globalization;
using System.Security.Claims;
using Lams.Domain.Users;
using Lams.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lams.Infrastructure.Identity;

/// <summary>
/// Sign-in rules on top of Identity:
/// <list type="bullet">
/// <item>only users whose staff record exists and is active can sign in;</item>
/// <item>the original sign-in time (<see cref="LamsSessionProperties.SignedInAt"/>) is preserved whenever the cookie is
/// re-issued for the same user (password change, MFA enrolment, 2FA completion), so the absolute session lifetime
/// cannot be extended. A fresh sign-in sets it from the clock.</item>
/// </list>
/// </summary>
public sealed class LamsSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    LamsDbContext db,
    TimeProvider clock)
    : SignInManager<ApplicationUser>(
        userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!await base.CanSignInAsync(user))
        {
            return false;
        }

        var staffId = new UserId(user.Id);
        return await db.StaffMembers.AnyAsync(s => s.Id == staffId && s.IsActive);
    }

    /// <summary>
    /// Completes a sign-in after <see cref="SignInManager{TUser}.CheckPasswordSignInAsync"/> succeeded and the account
    /// checks (temporary password expiry) passed: issues the application cookie, or starts the two-factor step.
    /// Never persistent ("remember me" is not offered).
    /// </summary>
    public Task<SignInResult> SignInAfterPasswordCheckAsync(ApplicationUser user) =>
        SignInOrTwoFactorAsync(user, isPersistent: false);

    public override async Task SignInWithClaimsAsync(
        ApplicationUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        ArgumentNullException.ThrowIfNull(user);
        authenticationProperties ??= new AuthenticationProperties();

        var signedInAt = await FindCurrentSessionStartAsync(user) ?? clock.GetUtcNow();
        authenticationProperties.Items[LamsSessionProperties.SignedInAt] =
            signedInAt.ToString("O", CultureInfo.InvariantCulture);

        await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
    }

    /// <summary>Reads the sign-in time of the current session if it belongs to the same user.</summary>
    private async Task<DateTimeOffset?> FindCurrentSessionStartAsync(ApplicationUser user)
    {
        var current = await Context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!current.Succeeded
            || current.Principal.FindFirstValue(Options.ClaimsIdentity.UserIdClaimType) != user.Id.ToString())
        {
            return null;
        }

        return ReadSignedInAt(current.Properties);
    }

    /// <summary>Parses the stored sign-in time; <c>null</c> when absent or malformed.</summary>
    public static DateTimeOffset? ReadSignedInAt(AuthenticationProperties? properties) =>
        properties?.Items.TryGetValue(LamsSessionProperties.SignedInAt, out var value) == true
        && DateTimeOffset.TryParseExact(
            value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
