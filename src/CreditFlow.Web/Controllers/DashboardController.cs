using CreditFlow.Application.Dashboard;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace CreditFlow.Web.Controllers;

/// <summary>
/// Landing page. Each role lands on its own dashboard, taken from the role's <c>DashboardKind</c> (data, not a
/// role-name check): KPI tiles that link to the filtered lists and a short "needs your attention" list.
/// </summary>
public sealed class DashboardController(CurrentAccess currentAccess, DashboardService dashboards) : Controller
{
    [HttpGet("/")]
    [HttpGet("/Dashboard")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var profile = await currentAccess.GetAsync();
        var data = profile is null ? null : await dashboards.GetAsync(cancellationToken);
        if (profile is null || data is null)
        {
            return Forbid();
        }

        return View(new DashboardViewModel(profile, data));
    }
}
