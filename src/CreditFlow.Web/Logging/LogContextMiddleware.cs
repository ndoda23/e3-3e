using CreditFlow.Application.Common;
using Serilog.Context;

namespace CreditFlow.Web.Logging;

/// <summary>
/// Adds the signed-in user, the request path and the correlation id to every log event written while the request is
/// handled (the <c>UserName</c>, <c>RequestPath</c> and <c>CorrelationId</c> columns of the <c>Logs</c> table).
/// Runs after authentication, so the user is known.
/// </summary>
internal sealed class LogContextMiddleware(RequestDelegate next)
{
    public const string UserName = "UserName";
    public const string RequestPath = "RequestPath";
    public const string CorrelationId = "CorrelationId";

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser)
    {
        using (LogContext.PushProperty(UserName, currentUser.UserName))
        using (LogContext.PushProperty(RequestPath, context.Request.Path.Value))
        using (LogContext.PushProperty(CorrelationId, currentUser.CorrelationId))
        {
            await next(context);
        }
    }
}
