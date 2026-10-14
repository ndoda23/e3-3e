using System.Diagnostics;
using System.Security.Claims;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Users;

namespace CreditFlow.Web.Infrastructure;

/// <summary>The signed-in user and request context, taken from the current HTTP request.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private const int MaxUserAgentLength = 512;

    private HttpContext? Context => accessor.HttpContext;

    public UserId? UserId =>
        Context?.User is { Identity.IsAuthenticated: true } user
        && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? new UserId(id)
            : null;

    public string? UserName => Context?.User is { Identity.IsAuthenticated: true } user ? user.Identity?.Name : null;

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var agent = Context?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrEmpty(agent) ? null : agent[..Math.Min(agent.Length, MaxUserAgentLength)];
        }
    }

    public string? CorrelationId => Activity.Current?.TraceId.ToString() ?? Context?.TraceIdentifier;
}
