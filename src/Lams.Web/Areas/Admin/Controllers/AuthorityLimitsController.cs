using Lams.Application.AuthorityLimits;
using Lams.Application.Roles;
using Lams.Domain.AuthorityLimits;
using Lams.Domain.Common;
using Lams.Domain.Roles;
using Lams.Web.Areas.Admin.Models;
using Lams.Web.Authorization;
using Lams.Web.Infrastructure;
using Lams.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using P = Lams.Application.Authorization.Permissions;

namespace Lams.Web.Areas.Admin.Controllers;

/// <summary>
/// Admin › Authority limits: credit decision limits (per role, level and currency) and pricing exception limits (per
/// role). The rules (business roles only, the role's level rule, no duplicates, value ranges) are in the service.
/// Limits are read on every evaluation, so changes apply immediately.
/// </summary>
[HasPermission(P.Admin.AuthorityLimitsManage)]
public sealed class AuthorityLimitsController(
    AuthorityLimitAdministrationService limits,
    RoleAdministrationService roles,
    IStringLocalizer<SharedResource> localizer) : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await limits.GetOverviewAsync(cancellationToken));

    // ----- Credit decision -----

    [HttpGet]
    public async Task<IActionResult> CreateCredit(CancellationToken cancellationToken) =>
        View("CreditForm", await WithRolesAsync(new CreditLimitForm(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> CreateCredit(CreditLimitForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (RequireRole(form.RoleId) && ModelState.IsValid)
        {
            var result = await limits.CreateCreditDecisionAsync(form.ToCreateCommand(), cancellationToken);
            if (result.IsSuccess)
            {
                return Saved("AuthorityLimits.Created");
            }

            ModelState.AddError(result.Error!, localizer);
        }

        return View("CreditForm", await WithRolesAsync(form, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> EditCredit(Guid id, CancellationToken cancellationToken)
    {
        var item = await FindAsync(id, LimitType.CreditDecision, cancellationToken);
        return item is null
            ? NotFound()
            : View("CreditForm", await WithRolesAsync(CreditLimitForm.From(item), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> EditCredit(Guid id, CreditLimitForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var item = await FindAsync(id, LimitType.CreditDecision, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            var result = await limits.UpdateCreditDecisionAsync(
                new UpdateCreditDecisionLimitCommand(item.Id, form.MaxAmount ?? 0m), cancellationToken);
            if (result.IsSuccess)
            {
                return Saved("AuthorityLimits.Saved");
            }

            ModelState.AddError(result.Error!, localizer);
        }

        // Role, level and currency identify the row and are not editable: show the stored ones.
        var model = CreditLimitForm.From(item);
        model.MaxAmount = form.MaxAmount;
        return View("CreditForm", await WithRolesAsync(model, cancellationToken));
    }

    // ----- Pricing exception -----

    [HttpGet]
    public async Task<IActionResult> CreatePricing(CancellationToken cancellationToken) =>
        View("PricingForm", await WithRolesAsync(new PricingLimitForm(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> CreatePricing(PricingLimitForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (RequireRole(form.RoleId) && ModelState.IsValid)
        {
            var result = await limits.CreatePricingExceptionAsync(
                new CreatePricingExceptionLimitCommand(new RoleId(form.RoleId!.Value), form.MaxRateGapPp, form.MinCustomerRoaePct),
                cancellationToken);
            if (result.IsSuccess)
            {
                return Saved("AuthorityLimits.Created");
            }

            ModelState.AddError(result.Error!, localizer);
        }

        return View("PricingForm", await WithRolesAsync(form, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> EditPricing(Guid id, CancellationToken cancellationToken)
    {
        var item = await FindAsync(id, LimitType.PricingException, cancellationToken);
        return item is null
            ? NotFound()
            : View("PricingForm", await WithRolesAsync(PricingLimitForm.From(item), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> EditPricing(Guid id, PricingLimitForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var item = await FindAsync(id, LimitType.PricingException, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            var result = await limits.UpdatePricingExceptionAsync(
                new UpdatePricingExceptionLimitCommand(item.Id, form.MaxRateGapPp, form.MinCustomerRoaePct),
                cancellationToken);
            if (result.IsSuccess)
            {
                return Saved("AuthorityLimits.Saved");
            }

            ModelState.AddError(result.Error!, localizer);
        }

        var model = PricingLimitForm.From(item);
        model.MaxRateGapPp = form.MaxRateGapPp;
        model.MinCustomerRoaePct = form.MinCustomerRoaePct;
        return View("PricingForm", await WithRolesAsync(model, cancellationToken));
    }

    // ----- Delete -----

    [HttpGet]
    public async Task<IActionResult> ConfirmDelete(Guid id, CancellationToken cancellationToken)
    {
        var item = (await limits.GetOverviewAsync(cancellationToken)).All().FirstOrDefault(i => i.Id.Value == id);
        if (item is null)
        {
            return NotFound();
        }

        var model = new ConfirmModel(
            localizer["AuthorityLimits.Delete.Title"].Value,
            Describe(item),
            localizer[item.LimitType == LimitType.CreditDecision
                ? "AuthorityLimits.Delete.CreditDescription"
                : "AuthorityLimits.Delete.PricingDescription"].Value,
            Url.Action(nameof(Delete), new { id })!,
            Url.Action(nameof(Index))!,
            localizer["AuthorityLimits.Delete.Button"].Value,
            "alert-triangle",
            IsDangerous: true);
        return Request.IsHtmx() ? PartialView("_ConfirmModal", model) : View("Confirm", model);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await limits.DeleteAsync(new AuthorityLimitId(id), cancellationToken);
        return result.IsSuccess ? Saved("AuthorityLimits.Deleted") : NotFound();
    }

    // ----- Helpers -----

    private string Describe(AuthorityLimitItem item)
    {
        var parts = new List<string> { $"{item.RoleName} ({item.RoleCode})" };
        if (item.AuthorityLevel is { } level)
        {
            parts.Add(localizer[$"AuthorityLevel.{level}"].Value);
        }

        if (item.Currency is { } currency)
        {
            parts.Add(currency.ToString());
        }

        return string.Join(" · ", parts);
    }

    private RedirectToActionResult Saved(string noticeKey)
    {
        TempData["Notice"] = localizer[noticeKey].Value;
        return RedirectToAction(nameof(Index));
    }

    private bool RequireRole(Guid? roleId)
    {
        if (roleId is null)
        {
            ModelState.AddModelError(nameof(CreditLimitForm.RoleId), localizer["AuthorityLimits.RoleRequired"].Value);
            return false;
        }

        return true;
    }

    private async Task<AuthorityLimitItem?> FindAsync(Guid id, LimitType type, CancellationToken cancellationToken) =>
        (await limits.GetOverviewAsync(cancellationToken)).All()
        .FirstOrDefault(i => i.Id.Value == id && i.LimitType == type);

    private async Task<IReadOnlyList<RoleOption>> BusinessRolesAsync(CancellationToken cancellationToken) =>
        (await roles.ListAsync(cancellationToken))
        .Where(r => !r.Profile.IsAdministrative)
        .Select(RoleOption.From)
        .OrderBy(r => r.Name)
        .ToList();

    private async Task<CreditLimitForm> WithRolesAsync(CreditLimitForm form, CancellationToken cancellationToken)
    {
        form.Roles = await BusinessRolesAsync(cancellationToken);
        return form;
    }

    private async Task<PricingLimitForm> WithRolesAsync(PricingLimitForm form, CancellationToken cancellationToken)
    {
        form.Roles = await BusinessRolesAsync(cancellationToken);
        return form;
    }
}

internal static class AuthorityLimitOverviewExtensions
{
    public static IEnumerable<AuthorityLimitItem> All(this AuthorityLimitOverview overview) =>
        overview.CreditDecision.Concat(overview.PricingException);
}
