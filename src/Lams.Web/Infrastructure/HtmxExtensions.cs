namespace Lams.Web.Infrastructure;

public static class HtmxExtensions
{
    public const string RequestHeader = "HX-Request";
    public const string CurrentUrlHeader = "HX-Current-URL";
    public const string RedirectHeader = "HX-Redirect";
    public const string TriggerHeader = "HX-Trigger";
    public const string PushUrlHeader = "HX-Push-Url";

    /// <summary>
    /// Shows a toast after the htmx swap (<c>showToast</c> event handled by site.js). The JSON is ASCII-only
    /// (non-ASCII characters are escaped), so localized texts are safe in a header.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="message">Toast text.</param>
    /// <param name="tone"><c>success</c>, <c>warning</c>, <c>danger</c> or empty.</param>
    public static void ShowToast(this HttpResponse response, string message, string tone = "success")
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers[TriggerHeader] = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, object> { ["showToast"] = new { message, tone } });
    }

    /// <summary>Puts <paramref name="url"/> in the address bar after an htmx swap (bookmarkable list filters).</summary>
    public static void PushUrl(this HttpResponse response, string url)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers[PushUrlHeader] = url;
    }

    /// <summary>True for requests made by htmx (partial page updates).</summary>
    public static bool IsHtmx(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.Equals(request.Headers[RequestHeader], "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The local path and query of the page that issued the htmx request (<c>HX-Current-URL</c>), or <c>null</c> when
    /// the header is missing or points to another origin. Used to send the user back to the page, not to the partial.
    /// </summary>
    public static string? HtmxCurrentLocalUrl(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Uri.TryCreate(request.Headers[CurrentUrlHeader], UriKind.Absolute, out var url)
            || !string.Equals(url.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var local = url.PathAndQuery;
        return local.StartsWith('/') && !local.StartsWith("//", StringComparison.Ordinal) && !local.StartsWith("/\\", StringComparison.Ordinal)
            ? local
            : null;
    }
}
