using CreditFlow.Web.Authorization;
using Microsoft.AspNetCore.Mvc;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Controllers;

// Placeholder pages for the Phase 2+ navigation targets. Each is protected by the same permissions as its menu entry.

[HasAnyPermission(P.Applications.ViewFull, P.Aml.ViewScreening, P.Pricing.ViewExceptions)]
public sealed class ApplicationsController : Controller
{
    [HttpGet]
    public IActionResult Index() => View("ComingSoon", "Applications");
}

[HasPermission(P.Applications.ViewFull)]
public sealed class PackagesController : Controller
{
    [HttpGet]
    public IActionResult Index() => View("ComingSoon", "Packages");
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
