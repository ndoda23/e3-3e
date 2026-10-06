using System.Security.Claims;
using Lams.Application.Auditing;
using Lams.Application.Common;
using Lams.Domain.Auditing;
using Lams.Domain.Users;
using Lams.Infrastructure.Identity;
using Lams.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Lams.Web.Authentication;

/// <summary>
/// Events of the application cookie.
/// <list type="bullet">
/// <item><b>ValidatePrincipal</b>: first Identity's <see cref="SecurityStampValidator"/> (deactivation, role change,
/// forced sign-out); only if the principal survives, the absolute session lifetime is enforced from the original
/// sign-in time. A missing sign-in time is treated as expired (fail closed).</item>
/// <item><b>RedirectToLogin / RedirectToAccessDenied</b>: htmx requests get 401 / 403 with an <c>HX-Redirect</c> header and
/// an empty body, so a login page is never swapped into a partial. Other requests get the normal 302.</item>
/// </list>
/// </summary>
public sealed class LamsCookieEvents(IOptions<SecurityOptions> securityOptions, TimeProvider clock) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var signedInAt = LamsSignInManager.ReadSignedInAt(context.Properties);
        if (!SessionLifetimePolicy.IsExpired(signedInAt, clock.GetUtcNow(), securityOptions.Value.AbsoluteSessionLifetime))
        {
            return;
        }

        var principal = context.Principal;
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        await AuditSessionExpiredAsync(context.HttpContext, principal);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.IsHtmx())
        {
            return base.RedirectToLogin(context);
        }

        var loginPath = context.Options.LoginPath.Value ?? "/Account/Login";
        var returnUrl = context.Request.HtmxCurrentLocalUrl();
        var target = returnUrl is null
            ? loginPath
            : $"{loginPath}?{context.Options.ReturnUrlParameter}={Uri.EscapeDataString(returnUrl)}";

        SuppressStatusCodePage(context.HttpContext);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers[HtmxExtensions.RedirectHeader] = target;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.IsHtmx())
        {
            return base.RedirectToAccessDenied(context);
        }

        SuppressStatusCodePage(context.HttpContext);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers[HtmxExtensions.RedirectHeader] = context.Options.AccessDeniedPath.Value ?? "/Account/AccessDenied";
        return Task.CompletedTask;
    }

    /// <summary>An htmx auth response must have an empty body: no re-executed error page swapped into a partial.</summary>
    private static void SuppressStatusCodePage(HttpContext httpContext)
    {
        if (httpContext.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
        {
            statusCodePages.Enabled = false;
        }
    }

    private static async Task AuditSessionExpiredAsync(HttpContext httpContext, ClaimsPrincipal principal)
    {
        var audit = httpContext.RequestServices.GetRequiredService<IAuditWriter>();
        var userId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (Guid?)null;

        await audit.WriteAsync(
            new AuditEvent(AuditActions.SessionExpired)
            {
                UserId = userId,
                UserName = principal.Identity?.Name,
                EntityType = "User",
                EntityId = userId?.ToString(),
            },
            httpContext.RequestAborted);
    }
}
