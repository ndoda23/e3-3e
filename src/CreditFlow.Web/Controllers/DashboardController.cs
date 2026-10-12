using CreditFlow.Domain.Roles;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace CreditFlow.Web.Controllers;

/// <summary>
/// Landing page. Each role lands on its own dashboard, taken from the role's <see cref="DashboardKind"/>
/// (data, not a role-name check). Administrators land on Admin › Users.
/// </summary>
public sealed class DashboardController(CurrentAccess currentAccess) : Controller
{
    [HttpGet("/")]
    [HttpGet("/Dashboard")]
    public async Task<IActionResult> Index()
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

        return View(new DashboardViewModel(profile));
    }
}
