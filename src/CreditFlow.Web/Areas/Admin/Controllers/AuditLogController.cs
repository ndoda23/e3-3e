using CreditFlow.Application.Auditing;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Users;
using CreditFlow.Web.Areas.Admin.Models;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Areas.Admin.Controllers;

/// <summary>
/// Admin › Audit log: a read-only viewer (filters, paging, details with before / after). There is deliberately no
/// action that changes or deletes records (the log is append-only in code and in the database).
/// <para>
/// Not an <see cref="AdminController"/>: besides the administrator (<c>admin.audit.view</c>), read-only roles such as the
/// Auditor (<c>audit.view</c>) open it without holding any administration permission.
/// </para>
/// </summary>
[Area("Admin")]
[HasAnyPermission(P.Admin.AuditView, P.Audit.View)]
public sealed class AuditLogController(IAuditLogQueries auditLog, IUserDirectory directory) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AuditListQuery query, CancellationToken cancellationToken) =>
        View(await BuildIndexAsync(query, cancellationToken));

    /// <summary>The list body (htmx) for the current filters.</summary>
    [HttpGet]
    public async Task<IActionResult> Rows([FromQuery] AuditListQuery query, CancellationToken cancellationToken)
    {
        if (!Request.IsHtmx())
        {
            return RedirectToAction(nameof(Index), query.RouteValues());
        }

        var model = await BuildIndexAsync(query, cancellationToken);
        Response.PushUrl(Url.Action(nameof(Index), model.Query.RouteValues())!);
        return PartialView("_AuditRows", model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var record = await auditLog.GetAsync(new AuditLogId(id), cancellationToken);
        return record is null
            ? NotFound()
            : View(new AuditDetailsViewModel(record, AuditChanges.Build(record.BeforeJson, record.AfterJson)));
    }

    private async Task<AuditIndexViewModel> BuildIndexAsync(AuditListQuery query, CancellationToken cancellationToken)
    {
        var page = await auditLog.SearchAsync(query.ToSearch(), cancellationToken);
        if (page.Page > page.TotalPages)
        {
            query.Page = page.TotalPages;
            page = await auditLog.SearchAsync(query.ToSearch(), cancellationToken);
        }

        var userName = query.User is { } user
            ? (await directory.GetDetailsAsync(new UserId(user), cancellationToken))?.FullName
            : null;

        return new AuditIndexViewModel(
            query,
            page,
            await auditLog.ListActionsAsync(cancellationToken),
            await auditLog.ListEntityTypesAsync(cancellationToken),
            userName);
    }
}
