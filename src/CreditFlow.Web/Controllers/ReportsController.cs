using CreditFlow.Application.Applications;
using CreditFlow.Application.Common;
using CreditFlow.Application.Reports;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Models;
using Microsoft.AspNetCore.Mvc;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Controllers;

/// <summary>
/// Reports for a period (default: the last 90 days) and optionally one product, within the viewer's data scope, with
/// Excel (every report) and CSV (one report) export. Viewing needs <c>reports.view</c>, exporting <c>reports.export</c>.
/// </summary>
[HasPermission(P.Reports.View)]
public sealed class ReportsController(ReportService reports, ApplicationService applications, TimeProvider clock) : Controller
{
    private const int DefaultDays = 90;

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, Guid? product, CancellationToken cancellationToken)
    {
        var filter = Filter(from, to, product);
        var tables = await reports.BuildAsync(filter, cancellationToken);
        if (tables.IsFailure)
        {
            return Forbid();
        }

        return View(new ReportsViewModel(filter, tables.Value, await applications.ListProductsAsync(cancellationToken)));
    }

    [HttpGet]
    [HasPermission(P.Reports.Export)]
    public async Task<IActionResult> Export(
        ExportFormat format, ReportKind report, DateOnly? from, DateOnly? to, Guid? product, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(format) || !Enum.IsDefined(report))
        {
            return BadRequest();
        }

        var file = await reports.ExportAsync(Filter(from, to, product), format, report, cancellationToken);
        return file.IsSuccess ? File(file.Value.Content, file.Value.ContentType, file.Value.FileName) : Forbid();
    }

    private ReportFilter Filter(DateOnly? from, DateOnly? to, Guid? product)
    {
        var today = BankTime.DateOf(clock.GetUtcNow());
        var end = to ?? today;
        return new ReportFilter(from ?? end.AddDays(-DefaultDays), end, product is { } id ? new LoanProductId(id) : null).Normalize();
    }
}
