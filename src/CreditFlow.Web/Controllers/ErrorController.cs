using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreditFlow.Web.Controllers;

/// <summary>
/// Friendly error pages, re-executed by <c>UseExceptionHandler("/Error")</c> and
/// <c>UseStatusCodePagesWithReExecute("/Error/{0}")</c>. They use the minimal layout (no database access).
/// </summary>
[AllowAnonymous]
[Route("Error")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ErrorController : Controller
{
    [Route("")]
    public IActionResult ServerError()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View("ServerError");
    }

    [Route("{statusCode:int}")]
    public IActionResult Status(int statusCode)
    {
        Response.StatusCode = statusCode;
        return statusCode switch
        {
            StatusCodes.Status404NotFound => View("NotFound"),
            StatusCodes.Status429TooManyRequests => View("TooManyRequests"),
            >= 500 => View("ServerError"),
            _ => View("StatusCode", statusCode),
        };
    }
}
