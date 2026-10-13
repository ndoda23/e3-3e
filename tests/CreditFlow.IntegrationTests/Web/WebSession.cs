using System.Net;
using System.Text.RegularExpressions;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>
/// A browser-like client: keeps cookies (including the antiforgery cookie) and does not follow redirects.
/// A form is posted the way a browser submits it: the page is read, the form whose <c>action</c> is the target is
/// found, and that form's own <c>__RequestVerificationToken</c> is sent. If the rendered form has no token the test
/// fails — exactly as the browser would get a 400. htmx posts carry the token from the page's meta tag in the
/// <c>RequestVerificationToken</c> header, as site.js does. Nothing here skips or fakes antiforgery.
/// </summary>
public sealed partial class WebSession(HttpClient client) : IDisposable
{
    public const string TokenField = "__RequestVerificationToken";

    [GeneratedRegex("""<form\b(?<attrs>[^>]*)>(?<body>.*?)</form>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FormPattern();

    [GeneratedRegex("""\b(?<name>[a-z-]+)="(?<value>[^"]*)"|\b(?<name>[a-z-]+)='(?<value>[^']*)'""", RegexOptions.IgnoreCase)]
    private static partial Regex AttributePattern();

    [GeneratedRegex("""<input\b[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex InputPattern();

    [GeneratedRegex("""<meta name="request-verification-token" content="(?<token>[^"]+)""")]
    private static partial Regex MetaTokenPattern();

    public HttpClient Client { get; } = client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Task<HttpResponseMessage> GetAsync(string path) => Client.GetAsync(path, Ct);

    /// <summary>GETs a page (200 expected) and checks that every POST form on it carries an antiforgery token.</summary>
    public async Task<string> GetPageAsync(string path)
    {
        var response = await Client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(Ct);
        AssertPostFormsCarryTokens(html, path);
        return html;
    }

    /// <summary>Opens <paramref name="formPage"/> and submits its form that posts to <paramref name="action"/>.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string formPage, string action, IDictionary<string, string> fields) =>
        await SubmitFormAsync(await GetPageAsync(formPage), action, fields);

    /// <summary>Submits the form in <paramref name="html"/> (a page or an htmx-loaded modal) that posts to <paramref name="action"/>.</summary>
    public Task<HttpResponseMessage> SubmitFormAsync(string html, string action, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var token = FormToken(html, action);
        return Client.PostAsync(
            action,
            new FormUrlEncodedContent(fields.Append(KeyValuePair.Create(TokenField, token))),
            Ct);
    }

    /// <summary>Submits a multipart (file upload) form in <paramref name="html"/> that posts to <paramref name="action"/>.</summary>
    public Task<HttpResponseMessage> SubmitMultipartFormAsync(string html, string action, MultipartFormDataContent content)
    {
        content.Add(new StringContent(FormToken(html, action)), TokenField);
        return Client.PostAsync(action, content, Ct);
    }

    /// <summary>A POST with no antiforgery token at all (expected to be rejected).</summary>
    public Task<HttpResponseMessage> PostWithoutTokenAsync(string action, IDictionary<string, string> fields) =>
        Client.PostAsync(action, new FormUrlEncodedContent(fields), Ct);

    /// <summary>
    /// For authorization tests: a hand-crafted POST to an action whose form this user is (correctly) never shown,
    /// carrying the session's genuine token taken from the sign-out form on the dashboard. Antiforgery still validates
    /// it; only the authorization outcome is under test.
    /// </summary>
    public async Task<HttpResponseMessage> PostWithSessionTokenAsync(string action, IDictionary<string, string> fields)
    {
        var home = await Client.GetAsync("/", Ct);
        var page = home.StatusCode == HttpStatusCode.Redirect ? home.Headers.Location!.OriginalString : "/"; // Admin lands on /Admin
        var token = FormToken(await GetPageAsync(page), "/Account/Logout");
        return await Client.PostAsync(action, new FormUrlEncodedContent(fields.Append(KeyValuePair.Create(TokenField, token))), Ct);
    }

    /// <summary>A GET as htmx sends it (<c>HX-Request: true</c>).</summary>
    public Task<HttpResponseMessage> HtmxGetAsync(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("HX-Request", "true");
        return SendAndDisposeAsync(request);
    }

    /// <summary>
    /// A POST as htmx sends it from <paramref name="hostPage"/>: <c>HX-Request</c> plus the token from the page's
    /// meta tag in the <c>RequestVerificationToken</c> header (site.js).
    /// </summary>
    public Task<HttpResponseMessage> HtmxPostAsync(string path, IDictionary<string, string> fields, string hostPage)
    {
        var meta = MetaTokenPattern().Match(hostPage);
        Assert.True(meta.Success, "The host page has no request-verification-token meta tag; site.js could not send the htmx header.");
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("HX-Request", "true");
        request.Headers.Add("RequestVerificationToken", WebUtility.HtmlDecode(meta.Groups["token"].Value));
        return SendAndDisposeAsync(request);
    }

    public Task<HttpResponseMessage> LoginAsync(string login, string password, string? returnUrl = null)
    {
        var path = returnUrl is null ? "/Account/Login" : $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        return PostFormAsync(path, path, new Dictionary<string, string> { ["Login"] = login, ["Password"] = password });
    }

    /// <summary>The POST forms in <paramref name="html"/>: their (decoded) action and their token, if any.</summary>
    public static IReadOnlyList<(string Action, string? Token)> PostForms(string html) =>
        FormPattern().Matches(html)
            .Select(form => (Attributes: Attributes(form.Groups["attrs"].Value), Body: form.Groups["body"].Value))
            .Where(form => string.Equals(form.Attributes.GetValueOrDefault("method"), "post", StringComparison.OrdinalIgnoreCase))
            .Select(form => (NormalizeAction(form.Attributes.GetValueOrDefault("action") ?? string.Empty), FindToken(form.Body)))
            .ToList();

    /// <summary>The token of the form in <paramref name="html"/> that posts to <paramref name="action"/>; fails if there is none.</summary>
    public static string FormToken(string html, string action)
    {
        var forms = PostForms(html);
        var matching = forms.Where(f => string.Equals(f.Action, NormalizeAction(action), StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(
            matching.Count > 0,
            $"No POST form with action '{action}' on the page. Forms found: {string.Join(", ", forms.Select(f => f.Action))}");
        Assert.True(
            matching.TrueForAll(f => f.Token is not null),
            $"The form posting to '{action}' has no {TokenField}: a browser submitting it would get 400.");
        return matching[0].Token!;
    }

    public static void AssertPostFormsCarryTokens(string html, string page)
    {
        var missing = PostForms(html).Where(f => f.Token is null).Select(f => f.Action).ToList();
        Assert.True(missing.Count == 0, $"{page}: POST form(s) without {TokenField}: {string.Join(", ", missing)}");
    }

    public void Dispose() => Client.Dispose();

    private static string? FindToken(string formBody) =>
        InputPattern().Matches(formBody)
            .Select(input => Attributes(input.Value))
            .Where(attrs => attrs.GetValueOrDefault("name") == TokenField && !string.IsNullOrEmpty(attrs.GetValueOrDefault("value")))
            .Select(attrs => attrs["value"])
            .FirstOrDefault();

    private static Dictionary<string, string> Attributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in AttributePattern().Matches(tag))
        {
            attributes.TryAdd(attribute.Groups["name"].Value, WebUtility.HtmlDecode(attribute.Groups["value"].Value));
        }

        return attributes;
    }

    private static string NormalizeAction(string action) => Uri.UnescapeDataString(action);

    private async Task<HttpResponseMessage> SendAndDisposeAsync(HttpRequestMessage request)
    {
        using (request)
        {
            return await Client.SendAsync(request, Ct);
        }
    }
}
