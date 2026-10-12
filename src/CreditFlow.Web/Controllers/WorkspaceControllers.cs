using CreditFlow.Web.Authorization;
using Microsoft.AspNetCore.Mvc;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Controllers;

// Placeholder pages for navigation targets built in later steps. Each is protected by the same permission as its
// menu entry.

[HasPermission(P.Applications.View)]
public sealed class ApplicationsController : Controller
{
    [HttpGet]
    public IActionResult Index() => View("ComingSoon", "Applications");
}

[HasPermission(P.Reports.View)]
public sealed class ReportsController : Controller
{
    [HttpGet]
    public IActionResult Index() => View("ComingSoon", "Reports");
}

[HasPermission(P.Applications.Create)]
public sealed class NewApplicationController : Controller
{
    [HttpGet("/Applications/New")]
    public IActionResult Index() => View("ComingSoon", "NewApplication");
}
