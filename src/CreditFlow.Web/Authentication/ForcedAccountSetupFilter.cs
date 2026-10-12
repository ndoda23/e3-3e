using System.Security.Claims;
using CreditFlow.Application.Authorization;
using CreditFlow.Domain.Users;
using CreditFlow.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CreditFlow.Web.Authentication;

/// <summary>Marks endpoints that stay reachable while the user still has to change their password or enrol in MFA.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowDuringAccountSetupAttribute : Attribute;

/// <summary>
/// Global filter: a signed-in user who signed in with a temporary password is sent to change it first; a user whose
/// role or account requires MFA is then sent to set up an authenticator. Nothing else is reachable until done.
/// The state comes from the cached access profile (no database query per request).
/// </summary>
public sealed class ForcedAccountSetupFilter(IUserAccessProfileService profiles) : IAsyncActionFilter
{
    public const string ChangePasswordPath = "/Account/ChangePassword";
    public const string EnableAuthenticatorPath = "/Account/EnableAuthenticator";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var user = context.HttpContext.User;
        var exempt = context.ActionDescriptor.EndpointMetadata.OfType<AllowDuringAccountSetupAttribute>().Any();

        if (exempt
            || user.Identity?.IsAuthenticated != true
            || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            await next();
            return;
        }

        var state = await profiles.GetSecurityStateAsync(new UserId(id), context.HttpContext.RequestAborted);
        var target = state switch
        {
            { MustChangePassword: true } => ChangePasswordPath,
            { MustEnrolMfa: true } => EnableAuthenticatorPath,
            _ => null,
        };

        if (target is null)
        {
            await next();
            return;
        }

        if (context.HttpContext.Request.IsHtmx())
        {
            context.HttpContext.Response.Headers[HtmxExtensions.RedirectHeader] = target;
            context.Result = new NoContentResult();
            return;
        }

        context.Result = new RedirectResult(target);
    }
}
