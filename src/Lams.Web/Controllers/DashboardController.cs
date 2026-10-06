using Lams.Application.Authorization;
using Lams.Domain.Roles;
using Lams.Web.Authorization;
using Lams.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Lams.Web.Controllers;

/// <summary>
/// Landing page. Each role lands on its own dashboard (FRD M1), taken from the role's <see cref="DashboardKind"/>
/// (data, not a role-name check). Administrators land on Admin › Users.
/// </summary>
public sealed class DashboardController(CurrentAccess currentAccess, IAuthorityLimitEvaluator authorityLimits) : Controller
{
    [HttpGet("/")]
    [HttpGet("/Dashboard")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var profile = await currentAccess.GetAsync();
        if (profile is null)
        {
            return Forbid();
        }

        if (profile.Dashboard == DashboardKind.Administration)
        {
            return RedirectToAction("Index", "Users", new { area = "Admin" });
        }

        // Deciders (underwriters, branch managers) see their own credit decision limit in the subtitle.
        var decides = profile.HasPermission(Permissions.Applications.Decide);
        var limits = decides
            ? await authorityLimits.ListCreditDecisionLimitsAsync(profile, cancellationToken)
            : [];
        return View(new DashboardViewModel(profile, decides, limits));
    }
}
