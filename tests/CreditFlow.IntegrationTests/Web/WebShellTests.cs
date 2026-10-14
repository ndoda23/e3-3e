using System.Net;
using CreditFlow.IntegrationTests.Infrastructure;
using CreditFlow.Web.Infrastructure;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Pipeline-level behaviour of the web shell (no sign-in needed).</summary>
[Collection(SqlServerCollectionDefinition.Name)]
public sealed class WebShellTests(SqlServerContainer sql) : IAsyncLifetime
{
    private CreditFlowWebFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _factory = new CreditFlowWebFactory(sql.NewDatabaseConnectionString());
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Health_endpoint_is_anonymous_and_healthy()
    {
        var response = await _factory.CreateBrowser().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Error/404")]
    [InlineData("/Error/500")]
    public async Task Sign_in_and_error_pages_carry_the_CreditFlow_brand(string path)
    {
        var html = await (await _factory.CreateBrowser().GetAsync(path, Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("<div class=\"brand-name\">CreditFlow</div>", html, StringComparison.Ordinal);
        Assert.Contains("· CreditFlow</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PCB LOS", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Credit Operations", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Security_headers_are_set_on_every_response()
    {
        var response = await _factory.CreateBrowser().GetAsync("/health", Ct);

        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.DoesNotContain("unsafe-inline", SecurityHeadersMiddleware.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Admin/Users")]
    [InlineData("/Reports")]
    public async Task Anonymous_page_requests_are_redirected_to_login(string path)
    {
        var response = await _factory.CreateBrowser().GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            $"https://localhost/Account/Login?ReturnUrl={Uri.EscapeDataString(path)}",
            response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Anonymous_htmx_request_gets_401_with_HX_Redirect_and_no_body()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Users?page=2");
        request.Headers.Add(HtmxExtensions.RequestHeader, "true");
        request.Headers.Add(HtmxExtensions.CurrentUrlHeader, "https://localhost/Admin/Users?search=tamta");

        var response = await _factory.CreateBrowser().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            "/Account/Login?ReturnUrl=" + Uri.EscapeDataString("/Admin/Users?search=tamta"),
            response.Headers.GetValues(HtmxExtensions.RedirectHeader).Single());
        Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Htmx_redirect_ignores_a_current_url_from_another_host()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Users");
        request.Headers.Add(HtmxExtensions.RequestHeader, "true");
        request.Headers.Add(HtmxExtensions.CurrentUrlHeader, "https://evil.example/phish");

        var response = await _factory.CreateBrowser().SendAsync(request, Ct);

        Assert.Equal("/Account/Login", response.Headers.GetValues(HtmxExtensions.RedirectHeader).Single());
    }

    [Fact]
    public async Task Unknown_pages_show_the_friendly_404_page()
    {
        // Static files and error pages are anonymous; unknown URLs reach the status-code page after authentication.
        var response = await _factory.CreateBrowser().GetAsync("/Error/404", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Static_assets_are_served_without_signing_in()
    {
        var browser = _factory.CreateBrowser();

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/css/site.css", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/lib/htmx/htmx.min.js", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/fonts/inter-latin-400-normal.woff2", Ct)).StatusCode);
    }
}
