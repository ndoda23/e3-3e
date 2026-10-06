using Lams.Web.Authorization;
using Microsoft.AspNetCore.Mvc;
using P = Lams.Application.Authorization.Permissions;

namespace Lams.Web.Areas.Admin.Controllers;

// Placeholders: CreditPolicy, Pricing and Settings stay placeholders in Phase 1.

[HasPermission(P.Admin.CreditPolicyManage)]
public sealed class CreditPolicyController : AdminController
{
    [HttpGet]
    public IActionResult Index() => View("ComingSoon", "CreditPolicy");
}

[HasPermission(P.Admin.PricingManage)]
public sealed class PricingController : AdminController
{
    [HttpGet]
    public IActionResult Index() => View("ComingSoon", "Pricing");
}

[HasPermission(P.Admin.SettingsManage)]
public sealed class SettingsController : AdminController
{
    [HttpGet]
    public IActionResult Index() => View("ComingSoon", "Settings");
}
