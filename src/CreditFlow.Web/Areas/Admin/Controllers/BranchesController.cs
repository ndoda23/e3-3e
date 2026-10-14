using CreditFlow.Application.Branches;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Common;
using CreditFlow.Web.Areas.Admin.Models;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using CreditFlow.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Areas.Admin.Controllers;

/// <summary>
/// Admin › Branches. The code is immutable; a branch with assigned users is deactivated, never deleted (it then
/// cannot be chosen for new assignments, but users who have it keep it). Only an unused branch can be deleted.
/// </summary>
[HasPermission(P.Admin.BranchesManage)]
public sealed class BranchesController(BranchAdministrationService branches, IStringLocalizer<SharedResource> localizer)
    : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View((await branches.ListAsync(includeInactive: true, cancellationToken))
            .OrderByDescending(b => b.IsActive).ThenBy(b => b.Name).ToList());

    [HttpGet]
    public IActionResult Create() => View("Form", new BranchForm());

    [HttpPost]
    public async Task<IActionResult> Create(BranchForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (ModelState.IsValid)
        {
            var result = await branches.CreateAsync(form.ToCreateCommand(), cancellationToken);
            if (result.IsSuccess)
            {
                return Saved("Branches.Created");
            }

            if (result.Error!.Code == BranchErrors.DuplicateCode.Code)
            {
                // The duplicate-code error carries no field; show it next to the code.
                ModelState.AddModelError(nameof(BranchForm.Code), ModelStateExtensions.Localize(result.Error, localizer));
            }
            else
            {
                ModelState.AddError(result.Error!, localizer);
            }
        }

        return View("Form", form);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var branch = await FindAsync(id, cancellationToken);
        return branch is null ? NotFound() : View("Form", BranchForm.From(branch));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, BranchForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var branch = await FindAsync(id, cancellationToken);
        if (branch is null)
        {
            return NotFound();
        }

        form.Existing = branch;
        form.Code = branch.Code;
        if (ModelState.IsValid)
        {
            var result = await branches.UpdateAsync(form.ToUpdateCommand(branch.Id), cancellationToken);
            if (result.IsSuccess)
            {
                return Saved("Branches.Saved");
            }

            ModelState.AddError(result.Error!, localizer);
        }

        return View("Form", form);
    }

    [HttpPost]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        await branches.SetActiveAsync(new BranchId(id), isActive: true, cancellationToken) is { IsSuccess: true }
            ? Saved("Branches.Activated")
            : NotFound();

    [HttpPost]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        await branches.SetActiveAsync(new BranchId(id), isActive: false, cancellationToken) is { IsSuccess: true }
            ? Saved("Branches.Deactivated")
            : NotFound();

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await branches.DeleteAsync(new BranchId(id), cancellationToken);
        if (result.IsSuccess)
        {
            return Saved("Branches.Deleted");
        }

        if (result.Error!.Type == ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData["Error"] = ModelStateExtensions.Localize(result.Error, localizer);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Confirmation for deactivating or deleting (modal with htmx, page without JavaScript).</summary>
    [HttpGet]
    public async Task<IActionResult> Confirm(Guid id, bool delete, CancellationToken cancellationToken)
    {
        var branch = await FindAsync(id, cancellationToken);
        if (branch is null)
        {
            return NotFound();
        }

        var key = delete ? "Branches.Delete" : "Branches.Deactivate";
        var model = new ConfirmModel(
            localizer[$"{key}.Title"].Value,
            $"{branch.Name} ({branch.Code}) · {branch.City}",
            localizer[$"{key}.Description", branch.AssignedUserCount].Value,
            Url.Action(delete ? nameof(Delete) : nameof(Deactivate), new { id })!,
            Url.Action(nameof(Index))!,
            localizer[$"{key}.Button"].Value,
            delete ? "x" : "building",
            IsDangerous: delete);
        return Request.IsHtmx() ? PartialView("_ConfirmModal", model) : View("Confirm", model);
    }

    private RedirectToActionResult Saved(string noticeKey)
    {
        TempData["Notice"] = localizer[noticeKey].Value;
        return RedirectToAction(nameof(Index));
    }

    private async Task<BranchSummary?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await branches.ListAsync(includeInactive: true, cancellationToken)).FirstOrDefault(b => b.Id.Value == id);
}
