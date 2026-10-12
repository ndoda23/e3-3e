using CreditFlow.Application.Roles;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;
using CreditFlow.Web.Areas.Admin.Models;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Areas.Admin.Controllers;

/// <summary>
/// Admin › Roles &amp; permissions: the permission matrix and the per-role editor. Segregation of duties (business vs
/// administrative permissions) and the permissions the administrator role must keep are enforced by the service;
/// the editor only mirrors them (disabled / locked checkboxes). Changes apply on the users' next request.
/// </summary>
[HasPermission(P.Admin.RolesManage)]
public sealed class RolesController(RoleAdministrationService roles, IStringLocalizer<SharedResource> localizer)
    : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(RoleIndexViewModel.Create(await roles.ListAsync(cancellationToken)));

    [HttpGet]
    public async Task<IActionResult> Permissions(Guid id, CancellationToken cancellationToken)
    {
        var editor = await roles.GetPermissionEditorAsync(new RoleId(id), cancellationToken);
        return editor.IsSuccess ? View(new RolePermissionsViewModel(editor.Value)) : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> Permissions(Guid id, string[]? permissions, CancellationToken cancellationToken)
    {
        var roleId = new RoleId(id);
        var selected = permissions ?? [];
        var result = await roles.UpdatePermissionsAsync(new UpdateRolePermissionsCommand(roleId, selected), cancellationToken);
        if (result.IsSuccess)
        {
            TempData["Notice"] = localizer["Roles.Saved"].Value;
            return RedirectToAction(nameof(Permissions), new { id });
        }

        if (result.Error!.Type == ErrorType.NotFound)
        {
            return NotFound();
        }

        // Show the error with the administrator's selection kept, so no edits are lost.
        var editor = await roles.GetPermissionEditorAsync(roleId, cancellationToken);
        if (editor.IsFailure)
        {
            return NotFound();
        }

        ModelState.AddError(result.Error, localizer);
        return View(RolePermissionsViewModel.WithSelection(editor.Value, selected));
    }

    [HttpPost]
    public async Task<IActionResult> RequireMfa(Guid id, bool require, CancellationToken cancellationToken)
    {
        var result = await roles.SetRequireMfaAsync(new RoleId(id), require, cancellationToken);
        if (result.IsFailure)
        {
            return NotFound();
        }

        TempData["Notice"] = localizer[require ? "Roles.MfaRequired" : "Roles.MfaNotRequired"].Value;
        return RedirectToAction(nameof(Permissions), new { id });
    }
}
