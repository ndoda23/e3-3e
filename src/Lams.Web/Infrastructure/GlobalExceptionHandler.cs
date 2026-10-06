using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Lams.Web.Infrastructure;

/// <summary>
/// Handles unexpected exceptions. Every exception is logged with the trace id. htmx and JSON callers get a
/// ProblemDetails response (no details leaked); normal page requests fall through to the friendly error page
/// (<c>/Error</c>, re-executed by <c>UseExceptionHandler</c>).
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        LogUnhandled(exception, httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);

        var wantsHtml = httpContext.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
        if (wantsHtml && !httpContext.Request.IsHtmx())
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Extensions = { ["traceId"] = httpContext.TraceIdentifier },
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path} (trace {TraceId})")]
    private partial void LogUnhandled(Exception exception, string method, string path, string traceId);
}
