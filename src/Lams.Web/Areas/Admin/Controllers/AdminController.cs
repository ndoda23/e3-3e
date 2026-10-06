using Lams.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lams.Web.Areas.Admin.Controllers;

/// <summary>
/// Base of every Admin-area controller: the user must hold at least one <c>admin.*</c> permission; each action adds
/// its own permission on top.
/// </summary>
[Area("Admin")]
[Authorize(Policy = Policies.Administration)]
public abstract class AdminController : Controller;
