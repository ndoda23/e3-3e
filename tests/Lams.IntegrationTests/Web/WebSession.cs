using System.Net;
using System.Text.RegularExpressions;

namespace Lams.IntegrationTests.Web;

/// <summary>A browser-like client: keeps cookies, reads antiforgery tokens from forms, does not follow redirects.</summary>
public sealed partial class WebSession(HttpClient client) : IDisposable
{
    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)"|value="(?<token>[^"]+)"[^>]*name="__RequestVerificationToken"|name="request-verification-token" content="(?<token>[^"]+)""")]
    private static partial Regex TokenPattern();

    public HttpClient Client { get; } = client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Task<HttpResponseMessage> GetAsync(string path) => Client.GetAsync(path, Ct);

    public async Task<string> GetPageAsync(string path)
    {
        var response = await Client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    /// <summary>Opens <paramref name="formPage"/>, then posts <paramref name="fields"/> to <paramref name="action"/> with its token.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string formPage, string action, IDictionary<string, string> fields)
    {
        var page = await GetPageAsync(formPage);
        return await PostWithTokenAsync(action, fields, Token(page));
    }

    public Task<HttpResponseMessage> PostWithTokenAsync(string action, IDictionary<string, string> fields, string? token)
    {
        var form = new Dictionary<string, string>(fields);
        if (token is not null)
        {
            form["__RequestVerificationToken"] = token;
        }

        return Client.PostAsync(action, new FormUrlEncodedContent(form), Ct);
    }

    /// <summary>Posts fields that may repeat (e.g. several checked checkboxes with the same name).</summary>
    public Task<HttpResponseMessage> PostPairsWithTokenAsync(
        string action, IEnumerable<KeyValuePair<string, string>> fields, string token) =>
        Client.PostAsync(
            action,
            new FormUrlEncodedContent(fields.Append(new KeyValuePair<string, string>("__RequestVerificationToken", token))),
            Ct);

    /// <summary>A GET as htmx sends it (<c>HX-Request: true</c>).</summary>
    public Task<HttpResponseMessage> HtmxGetAsync(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("HX-Request", "true");
        return SendAndDisposeAsync(request);
    }

    /// <summary>A POST as htmx sends it: <c>HX-Request</c> plus the antiforgery token in the header (site.js).</summary>
    public Task<HttpResponseMessage> HtmxPostAsync(string path, IDictionary<string, string> fields, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("HX-Request", "true");
        request.Headers.Add("RequestVerificationToken", token);
        return SendAndDisposeAsync(request);
    }

    public Task<HttpResponseMessage> LoginAsync(string login, string password, string? returnUrl = null) =>
        PostFormAsync(
            "/Account/Login",
            returnUrl is null ? "/Account/Login" : $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}",
            new Dictionary<string, string> { ["Login"] = login, ["Password"] = password });

    public static string Token(string html)
    {
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, "No antiforgery token found on the page.");
        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    public void Dispose() => Client.Dispose();

    private async Task<HttpResponseMessage> SendAndDisposeAsync(HttpRequestMessage request)
    {
        using (request)
        {
            return await Client.SendAsync(request, Ct);
        }
    }
}
