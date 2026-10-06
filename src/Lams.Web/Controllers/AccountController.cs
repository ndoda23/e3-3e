using Lams.Infrastructure.Identity;
using Lams.Web.Authentication;
using Lams.Web.Infrastructure;
using Lams.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Lams.Web.Controllers;

/// <summary>
/// Sign-in, two-factor step, forced password change, MFA set-up, sign-out. There is no self-registration: accounts are
/// created by administrators. Sign-in is never persistent. Return URLs are honoured only when they are local.
/// </summary>
[Route("Account")]
public sealed class AccountController(AccountService accounts, IStringLocalizer<SharedResource> localizer) : Controller
{
    private static readonly Dictionary<string, string> PasswordFields = new(StringComparer.Ordinal)
    {
        ["Password"] = nameof(ChangePasswordViewModel.NewPassword),
    };

    // ----- Sign-in -----

    [AllowAnonymous]
    [HttpGet("Login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [AllowAnonymous]
    [HttpPost("Login")]
    [EnableRateLimiting(WebComposition.SignInRateLimitPolicy)]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var status = await accounts.PasswordSignInAsync(model.Login, model.Password, cancellationToken);
        switch (status)
        {
            case SignInStatus.Succeeded:
                return RedirectToLocal(returnUrl);
            case SignInStatus.RequiresTwoFactor:
                return RedirectToAction(nameof(LoginWith2fa), new { returnUrl });
            case SignInStatus.LockedOut:
                return RedirectToAction(nameof(Lockout));
            case SignInStatus.TemporaryPasswordExpired:
                ModelState.AddModelError(string.Empty, localizer["Login.TemporaryPasswordExpired"]);
                return LoginFailedView(model);
            default:
                // One generic message for unknown user, wrong password and inactive account (no account enumeration).
                ModelState.AddModelError(string.Empty, localizer["Login.InvalidCredentials"]);
                return LoginFailedView(model);
        }
    }

    [AllowAnonymous]
    [HttpGet("LoginWith2fa")]
    public async Task<IActionResult> LoginWith2fa(string? returnUrl = null)
    {
        if (!await accounts.IsTwoFactorPendingAsync())
        {
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new TwoFactorViewModel());
    }

    [AllowAnonymous]
    [HttpPost("LoginWith2fa")]
    [EnableRateLimiting(WebComposition.SignInRateLimitPolicy)]
    public async Task<IActionResult> LoginWith2fa(TwoFactorViewModel model, string? returnUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!await accounts.IsTwoFactorPendingAsync())
        {
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var status = await accounts.TwoFactorSignInAsync(model.Code, cancellationToken);
        return SecondFactorResult(status, model, returnUrl);
    }

    [AllowAnonymous]
    [HttpGet("LoginWithRecoveryCode")]
    public async Task<IActionResult> LoginWithRecoveryCode(string? returnUrl = null)
    {
        if (!await accounts.IsTwoFactorPendingAsync())
        {
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new RecoveryCodeViewModel());
    }

    [AllowAnonymous]
    [HttpPost("LoginWithRecoveryCode")]
    [EnableRateLimiting(WebComposition.SignInRateLimitPolicy)]
    public async Task<IActionResult> LoginWithRecoveryCode(
        RecoveryCodeViewModel model, string? returnUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!await accounts.IsTwoFactorPendingAsync())
        {
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var status = await accounts.RecoveryCodeSignInAsync(model.RecoveryCode, cancellationToken);
        return SecondFactorResult(status, model, returnUrl);
    }

    [AllowAnonymous]
    [HttpGet("Lockout")]
    public IActionResult Lockout() => View();

    [AllowAnonymous]
    [HttpGet("AccessDenied")]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    /// <summary>POST only (antiforgery validated globally). Ends the session, clears the cookies and is audited.</summary>
    [AllowAnonymous]
    [AllowDuringAccountSetup]
    [HttpPost("Logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await accounts.SignOutAsync(User, cancellationToken);
        return RedirectToAction(nameof(Login));
    }

    // ----- Forced set-up after sign-in -----

    /// <summary>Change of a temporary password (forced right after signing in with one).</summary>
    [AllowDuringAccountSetup]
    [HttpGet("ChangePassword")]
    public IActionResult ChangePassword(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new ChangePasswordViewModel());
    }

    [AllowDuringAccountSetup]
    [HttpPost("ChangePassword")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordViewModel model, string? returnUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(new ChangePasswordViewModel());
        }

        var result = await accounts.ChangePasswordAsync(User, model.CurrentPassword, model.NewPassword, cancellationToken);
        if (result.IsFailure)
        {
            ModelState.AddError(result.Error!, localizer, PasswordFields);
            return View(new ChangePasswordViewModel());
        }

        return RedirectToLocal(returnUrl);
    }

    /// <summary>Authenticator app set-up (forced when the role or the account requires MFA; also reached from My account).</summary>
    [AllowDuringAccountSetup]
    [HttpGet("EnableAuthenticator")]
    public async Task<IActionResult> EnableAuthenticator(CancellationToken cancellationToken)
    {
        var model = await BuildAuthenticatorModelAsync(cancellationToken);
        return model is null ? RedirectToAction(nameof(Login)) : View(model);
    }

    [AllowDuringAccountSetup]
    [HttpPost("EnableAuthenticator")]
    public async Task<IActionResult> EnableAuthenticator(EnableAuthenticatorViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        var fresh = await BuildAuthenticatorModelAsync(cancellationToken);
        if (fresh is null)
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            return View(fresh);
        }

        var result = await accounts.EnableAuthenticatorAsync(User, model.Code, cancellationToken);
        if (result.IsFailure)
        {
            ModelState.AddError(result.Error!, localizer);
            return View(fresh);
        }

        return View("ShowRecoveryCodes", new RecoveryCodesViewModel(result.Value, "/"));
    }

    /// <summary>Re-shows the form without echoing the password back.</summary>
    private ViewResult LoginFailedView(LoginViewModel model)
    {
        model.Password = string.Empty;
        ModelState.Remove(nameof(LoginViewModel.Password));
        return View(nameof(Login), model);
    }

    private async Task<EnableAuthenticatorViewModel?> BuildAuthenticatorModelAsync(CancellationToken cancellationToken)
    {
        var setup = await accounts.GetAuthenticatorSetupAsync(User);
        var status = await accounts.GetTwoFactorStatusAsync(User, cancellationToken);
        return setup is null
            ? null
            : new EnableAuthenticatorViewModel
            {
                SharedKey = setup.SharedKey,
                AuthenticatorUri = setup.AuthenticatorUri,
                Required = status is { Required: true, Enabled: false },
            };
    }

    private IActionResult SecondFactorResult<TModel>(SignInStatus status, TModel model, string? returnUrl)
    {
        switch (status)
        {
            case SignInStatus.Succeeded:
                return RedirectToLocal(returnUrl);
            case SignInStatus.LockedOut:
                return RedirectToAction(nameof(Lockout));
            default:
                ModelState.AddModelError(string.Empty, localizer["Login.InvalidCode"]);
                return View(model);
        }
    }

    /// <summary>Only local return URLs are followed (open-redirect protection); otherwise the dashboard.</summary>
    private LocalRedirectResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : LocalRedirect("/");
}
