using System.Security.Claims;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.Web.Infrastructure;
using CreditFlow.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace CreditFlow.Web.Controllers;

/// <summary>"My account": profile (read-only), password change and two-factor authentication.</summary>
[Route("Account/Manage")]
public sealed class ManageController(
    AccountService accounts,
    IUserDirectory directory,
    IStringLocalizer<SharedResource> localizer) : Controller
{
    private static readonly Dictionary<string, string> PasswordFields = new(StringComparer.Ordinal)
    {
        ["Password"] = nameof(ChangePasswordViewModel.NewPassword),
    };

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            || await directory.GetDetailsAsync(new UserId(id), cancellationToken) is not { } details)
        {
            return Forbid();
        }

        return View(new MyAccountViewModel(details, await accounts.GetTwoFactorStatusAsync(User, cancellationToken)));
    }

    [HttpGet("ChangePassword")]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost("ChangePassword")]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
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

        TempData["Notice"] = localizer["Manage.PasswordChanged"].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("TwoFactor")]
    public async Task<IActionResult> TwoFactor(CancellationToken cancellationToken)
    {
        var status = await accounts.GetTwoFactorStatusAsync(User, cancellationToken);
        return status is null ? Forbid() : View(status);
    }

    [HttpPost("TwoFactor/Disable")]
    public async Task<IActionResult> DisableTwoFactor(CancellationToken cancellationToken)
    {
        var result = await accounts.DisableTwoFactorAsync(User, cancellationToken);
        TempData["Notice"] = result.IsSuccess
            ? localizer["Manage.TwoFactorDisabled"].Value
            : ModelStateExtensions.Localize(result.Error!, localizer);
        return RedirectToAction(nameof(TwoFactor));
    }

    [HttpGet("TwoFactor/Reset")]
    public IActionResult ResetAuthenticator() => View();

    [HttpPost("TwoFactor/Reset")]
    public async Task<IActionResult> ResetAuthenticatorConfirmed(CancellationToken cancellationToken)
    {
        var result = await accounts.ResetAuthenticatorAsync(User, cancellationToken);
        return result.IsSuccess
            ? Redirect("/Account/EnableAuthenticator")
            : RedirectToAction(nameof(TwoFactor));
    }

    [HttpPost("TwoFactor/RecoveryCodes")]
    public async Task<IActionResult> GenerateRecoveryCodes(CancellationToken cancellationToken)
    {
        var result = await accounts.GenerateRecoveryCodesAsync(User, cancellationToken);
        if (result.IsFailure)
        {
            TempData["Notice"] = ModelStateExtensions.Localize(result.Error!, localizer);
            return RedirectToAction(nameof(TwoFactor));
        }

        return View("ShowRecoveryCodes", new RecoveryCodesViewModel(result.Value, Url.Action(nameof(TwoFactor))!));
    }
}
