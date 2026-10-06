using Lams.Application.Auditing;
using Lams.Application.Branches;
using Lams.Application.Roles;
using Lams.Application.Users;
using Lams.Domain.Common;
using Lams.Domain.Users;
using Lams.Web.Areas.Admin.Models;
using Lams.Web.Authorization;
using Lams.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using P = Lams.Application.Authorization.Permissions;

namespace Lams.Web.Areas.Admin.Controllers;

/// <summary>
/// Admin › Users: the staff list (htmx filters, sort, paging), create / edit with role-driven fields, and the user
/// actions. Viewing needs <c>admin.users.view</c>; every change needs <c>admin.users.manage</c>. The guard rails
/// (no self-deactivation or self-demotion, at least one active administrator) are enforced by the service.
/// </summary>
[HasAnyPermission(P.Admin.UsersView, P.Admin.UsersManage)]
public sealed class UsersController(
    UserAdministrationService users,
    RoleAdministrationService roles,
    BranchAdministrationService branches,
    IAuditLogQueries auditLog,
    CurrentAccess currentAccess,
    IStringLocalizer<SharedResource> localizer) : AdminController
{
    private const int RecentActivityCount = 10;

    // ----- List -----

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] UserListQuery query, CancellationToken cancellationToken) =>
        View(await BuildIndexAsync(query, cancellationToken));

    /// <summary>The list body (htmx): table, count and pager for the current filters.</summary>
    [HttpGet]
    public async Task<IActionResult> Rows([FromQuery] UserListQuery query, CancellationToken cancellationToken)
    {
        if (!Request.IsHtmx())
        {
            return RedirectToAction(nameof(Index), query.RouteValues());
        }

        var model = await BuildIndexAsync(query, cancellationToken);
        Response.PushUrl(Url.Action(nameof(Index), model.Query.RouteValues())!);
        return PartialView("_UserRows", model);
    }

    // ----- Details -----

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var details = await users.GetDetailsAsync(new UserId(id), cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        var activity = await auditLog.ListRecentForUserAsync(details.Id, RecentActivityCount, cancellationToken);
        var profile = await currentAccess.GetAsync();
        return View(new UserDetailsViewModel(
            details, activity, profile?.UserId == details.Id, profile?.HasPermission(P.Admin.UsersManage) == true));
    }

    [HttpGet]
    public async Task<IActionResult> RecentActivity(Guid id, CancellationToken cancellationToken)
    {
        var userId = new UserId(id);
        var activity = await auditLog.ListRecentForUserAsync(userId, RecentActivityCount, cancellationToken);
        return PartialView("_RecentActivity", new RecentActivityViewModel(userId, activity));
    }

    // ----- Create / edit -----

    [HttpGet]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View(await WithLookupsAsync(new UserFormModel(), cancellationToken));

    [HttpPost]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> Create(UserFormModel form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (ModelState.IsValid)
        {
            var result = await users.CreateAsync(form.ToCreateCommand(), cancellationToken);
            if (result.IsSuccess)
            {
                var issued = result.Value;
                return TemporaryPasswordView(
                    new TemporaryPasswordModel(
                        issued.UserId, form.FullName!.Trim(), form.UserName!.Trim(), issued.TemporaryPassword,
                        issued.ExpiresAt, IsNewUser: true),
                    asModal: false);
            }

            ModelState.AddError(result.Error!, localizer);
        }

        return View(await WithLookupsAsync(form, cancellationToken));
    }

    [HttpGet]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var details = await users.GetDetailsAsync(new UserId(id), cancellationToken);
        return details is null
            ? NotFound()
            : View(await WithLookupsAsync(UserFormModel.From(details), cancellationToken));
    }

    [HttpPost]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> Edit(Guid id, UserFormModel form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var userId = new UserId(id);
        form.Existing = await users.GetDetailsAsync(userId, cancellationToken);
        if (form.Existing is null)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            var result = await users.UpdateAsync(form.ToUpdateCommand(userId), cancellationToken);
            if (result.IsSuccess)
            {
                TempData["Notice"] = localizer["Users.Saved"].Value;
                return RedirectToAction(nameof(Details), new { id });
            }

            ModelState.AddError(result.Error!, localizer);
        }

        return View(await WithLookupsAsync(form, cancellationToken));
    }

    /// <summary>The branch / segment / authority level fields for the chosen role (htmx, on role change).</summary>
    [HttpGet]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> RoleFields([FromQuery] UserFormModel form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        ModelState.Clear();
        return PartialView("_RoleAttributeFields", await WithLookupsAsync(form, cancellationToken));
    }

    // ----- Actions -----

    /// <summary>Confirmation for a sensitive action: a modal (htmx) or a page (without JavaScript).</summary>
    [HttpGet]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> Confirm(Guid id, UserOperation operation, CancellationToken cancellationToken)
    {
        var details = await users.GetDetailsAsync(new UserId(id), cancellationToken);
        if (details is null || !Enum.IsDefined(operation))
        {
            return NotFound();
        }

        var model = new ConfirmUserOperationModel(details.Id, details.FullName, operation);
        return Request.IsHtmx() ? PartialView("_ConfirmActionModal", model) : View(model);
    }

    [HttpPost]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        AfterAction(id, await users.DeactivateAsync(new UserId(id), cancellationToken), "Users.Deactivated");

    [HttpPost]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        AfterAction(id, await users.ActivateAsync(new UserId(id), cancellationToken), "Users.Activated");

    [HttpPost]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken) =>
        AfterAction(id, await users.UnlockAsync(new UserId(id), cancellationToken), "Users.Unlocked");

    [HttpPost]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> ForceSignOut(Guid id, CancellationToken cancellationToken) =>
        AfterAction(id, await users.ForceSignOutAsync(new UserId(id), cancellationToken), "Users.SignedOut");

    [HttpPost]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> RequireMfa(Guid id, bool require, CancellationToken cancellationToken) =>
        AfterAction(
            id,
            await users.SetRequireMfaAsync(new UserId(id), require, cancellationToken),
            require ? "Users.MfaRequired" : "Users.MfaNotRequired");

    [HttpPost]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken cancellationToken)
    {
        var userId = new UserId(id);
        var details = await users.GetDetailsAsync(userId, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        var result = await users.ResetPasswordAsync(userId, cancellationToken);
        if (result.IsFailure)
        {
            return AfterAction(id, result, string.Empty);
        }

        return TemporaryPasswordView(
            new TemporaryPasswordModel(
                userId, details.FullName, details.UserName, result.Value.TemporaryPassword, result.Value.ExpiresAt,
                IsNewUser: false),
            asModal: Request.IsHtmx());
    }

    /// <summary>The "Working today" switch: re-renders itself (htmx) or returns to the details page.</summary>
    [HttpPost]
    [HasPermission(P.Admin.UsersManage)]
    public async Task<IActionResult> Availability(Guid id, bool isAvailable, CancellationToken cancellationToken)
    {
        var userId = new UserId(id);
        var result = await users.SetAvailabilityAsync(userId, isAvailable, cancellationToken);
        if (!Request.IsHtmx())
        {
            return AfterAction(id, result, "Users.AvailabilitySaved");
        }

        var details = await users.GetDetailsAsync(userId, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        Response.ShowToast(
            result.IsSuccess
                ? localizer[isAvailable ? "Users.MarkedWorking" : "Users.MarkedUnavailable", details.FullName].Value
                : ModelStateExtensions.Localize(result.Error!, localizer),
            result.IsSuccess ? (isAvailable ? "success" : "warning") : "danger");
        return PartialView(
            "_AvailabilityToggle",
            new AvailabilityToggleModel(details.Id, details.FullName, details.IsAvailable, details.IsActive, CanManage: true));
    }

    // ----- Helpers -----

    private IActionResult AfterAction(Guid id, Result result, string noticeKey)
    {
        if (result.IsSuccess)
        {
            TempData["Notice"] = localizer[noticeKey].Value;
        }
        else if (result.Error!.Type == ErrorType.NotFound)
        {
            return NotFound();
        }
        else
        {
            TempData["Error"] = ModelStateExtensions.Localize(result.Error, localizer);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>The one-time password must never be cached by the browser or a proxy.</summary>
    private IActionResult TemporaryPasswordView(TemporaryPasswordModel model, bool asModal)
    {
        Response.Headers.CacheControl = "no-store";
        return asModal ? PartialView("_TempPasswordModal", model) : View("TemporaryPassword", model);
    }

    private async Task<UserIndexViewModel> BuildIndexAsync(UserListQuery query, CancellationToken cancellationToken)
    {
        var page = await users.SearchAsync(query.ToSearch(), cancellationToken);

        // Stay on the last page when filters shrink the result (e.g. page 3 of a narrower search).
        if (page.Page > page.TotalPages)
        {
            query.Page = page.TotalPages;
            page = await users.SearchAsync(query.ToSearch(), cancellationToken);
        }

        var profile = await currentAccess.GetAsync();
        return new UserIndexViewModel(
            query,
            page,
            (await roles.ListAsync(cancellationToken)).Select(RoleOption.From).OrderBy(r => r.Name).ToList(),
            (await branches.ListAsync(includeInactive: true, cancellationToken)).Select(BranchOption.From)
                .OrderBy(b => b.Name).ToList(),
            profile?.UserId,
            profile?.HasPermission(P.Admin.UsersManage) == true);
    }

    private async Task<UserFormModel> WithLookupsAsync(UserFormModel form, CancellationToken cancellationToken)
    {
        form.Roles = (await roles.ListAsync(cancellationToken)).Select(RoleOption.From).OrderBy(r => r.Name).ToList();
        form.Branches = (await branches.ListAsync(includeInactive: true, cancellationToken)).Select(BranchOption.From)
            .OrderBy(b => b.Name).ToList();
        return form;
    }
}
