using System.Net;
using System.Text.RegularExpressions;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Roles;
using CreditFlow.Infrastructure.Outbox;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>
/// Every POST the UI can make must pass antiforgery the way a browser sends it. Each role browses the Development demo
/// data (applications in every status, loans, clients, administration) by following links and htmx GETs;
/// every rendered POST form must contain its own <c>__RequestVerificationToken</c>, and every POST endpoint must be
/// reached by a rendered form or hx-post, so a new action cannot silently escape the check.
/// </summary>
public sealed partial class AntiforgeryFormTests(SqlServerContainer sql) : WebTestBase(sql)
{
    private const int MaxPagesPerRole = 1500;
    private const int MaxQueryVariantsPerPath = 8;

    /// <summary>POST endpoints whose page a signed-in session does not browse to; each is posted from its rendered form elsewhere.</summary>
    private static readonly Dictionary<string, string> NotReachedByCrawl = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/Account/Login"] = "anonymous page; every test signs in through its form (WebSession.LoginAsync)",
        ["/Account/LoginWith2fa"] = "MFA sign-in; AccountTests posts its form",
        ["/Account/LoginWithRecoveryCode"] = "MFA sign-in; AccountTests posts its form",
        ["/Account/ChangePassword"] = "forced first-sign-in change; AccountTests and DefinitionOfDoneTests post its form",
        ["/Account/EnableAuthenticator"] = "MFA set-up; AccountTests posts its form",

        // Known gap (PLAN.md): the demo data has no record in the state that renders these forms.
        ["/Account/Manage/TwoFactor/Disable"] = "known gap: needs a user with MFA turned on",
        ["/Account/Manage/TwoFactor/RecoveryCodes"] = "known gap: needs a user with MFA turned on",
        ["/Account/Manage/TwoFactor/Reset"] = "known gap: needs a user with MFA turned on",
        ["/Admin/Branches/Activate/{id}"] = "known gap: needs an inactive branch",
        ["/Admin/Products/Activate/{id}"] = "known gap: needs an inactive product",
        ["/Admin/Users/Activate/{id}"] = "known gap: needs an inactive user",
        ["/Admin/Users/Unlock/{id}"] = "known gap: needs a locked user",
        ["/Applications/RemoveCollateral/{id}"] = "known gap: needs a draft with collateral",
        ["/Applications/RemoveParty/{id}"] = "known gap: needs a draft with a co-borrower or guarantor",
        ["/Clients/RemoveObligation/{id}"] = "known gap: needs a client with an obligation",
        ["/Loans/RepayEarly/{id}"] = "known gap: rendered only after an early-repayment quote (GET form)",
    };

    protected override string Environment => "Development";

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["Seed:DevUsers:Enabled"] = "true",
        ["Seed:DevUsers:Password"] = InfrastructureHost.DevPassword,
        ["Seed:DemoData:Enabled"] = "true",
        ["Mocks:BureauTimeout"] = "00:00:00.200",
        ["DatabaseLog:BatchPeriod"] = "00:00:00.200",
    };

    [GeneratedRegex("""\b(?<kind>href|hx-get)="(?<url>/[^"#]*)""")]
    private static partial Regex LinkPattern();

    [GeneratedRegex("""\bhx-post="(?<url>/[^"]*)""")]
    private static partial Regex HtmxPostPattern();

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex GuidPattern();

    [Fact]
    public async Task Every_rendered_post_form_carries_its_token_and_every_post_endpoint_has_one()
    {
        // The live pre-check moves demo applications on to ComplianceReview / DocumentsPending.
        await Factory.Services.GetRequiredService<OutboxProcessor>().ProcessAllDueAsync(Ct);

        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var crawls = new List<string>();

        foreach (var (role, branch) in new (string, string?)[]
        {
            (RoleCodes.Operator, BranchSeedCatalog.Vake),
            (RoleCodes.Operator, BranchSeedCatalog.Batumi),
            (RoleCodes.Underwriter, null),
            (RoleCodes.SeniorUnderwriter, null),
            (RoleCodes.CreditCommittee, null),
            (RoleCodes.Compliance, null),
            (RoleCodes.Auditor, null),
            (RoleCodes.Admin, null),
        })
        {
            var (userName, _, password) = await CreateUserAsync(role, branch);
            using var browser = NewSession();
            Assert.Equal(HttpStatusCode.Redirect, (await browser.LoginAsync(userName, password)).StatusCode);
            var pages = await CrawlAsync(browser, $"{role}/{branch}", reached, missing);
            crawls.Add($"{role}/{branch}: {pages} pages");
        }

        Assert.True(missing.Count == 0, "POST forms without __RequestVerificationToken:\n" + string.Join("\n", missing.Distinct()));

        // The whole click-through logged no warning or error, except the one-line retries of a mock service that is down.
        var warnings = await WarningsLoggedAsync();
        Assert.True(warnings.Count == 0, "Unexpected warnings or errors during the click-through:\n" + string.Join("\n", warnings));
        var unreached = PostEndpoints().Where(e => !reached.Contains(e) && !NotReachedByCrawl.ContainsKey(e)).ToList();
        Assert.True(
            unreached.Count == 0,
            $"POST endpoints no crawled page renders a form for ({string.Join(", ", crawls)}):\n" + string.Join("\n", unreached));
    }

    /// <summary>Warnings and errors in the <c>Logs</c> table, apart from transient retries (flushed by a marker first).</summary>
    private async Task<List<string>> WarningsLoggedAsync()
    {
        var marker = Guid.NewGuid().ToString("N");
        LogMarker(Factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CreditFlow.Tests"), marker);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!await QueryAsync(db => db.Logs.AnyAsync(l => l.Message!.Contains(marker), Ct)) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        }

        return await QueryAsync(db => db.Logs.AsNoTracking()
            .Where(l => l.Level == "Warning" || l.Level == "Error" || l.Level == "Fatal")
            .Where(l => !l.Message!.Contains(marker) && !l.Message.Contains("will be retried"))
            .OrderBy(l => l.Id)
            .Select(l => l.Level + ": " + l.Message)
            .ToListAsync(Ct));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Marker {Marker}")]
    private static partial void LogMarker(ILogger logger, string marker);

    [Fact]
    public async Task Create_application_posts_with_the_token_from_its_own_form()
    {
        var (userName, _, password) = await CreateOperatorAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);
        var page = await browser.GetPageAsync("/Applications/New");

        // Not the sign-out form's token, not the meta tag: the Create Application form must carry its own.
        Assert.False(string.IsNullOrEmpty(WebSession.FormToken(page, "/Applications/New")));
        var response = await browser.SubmitFormAsync(page, "/Applications/New", new Dictionary<string, string>
        {
            ["BorrowerPersonalNumber"] = "01234567890",
            ["Amount"] = "5000",
            ["Currency"] = "GEL",
            ["TermMonths"] = "24",
            ["AnnualRate"] = "18",
            ["ScheduleType"] = "Annuity",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the form again, with validation messages
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostWithoutTokenAsync("/Applications/New", new Dictionary<string, string>())).StatusCode);
    }

    private static async Task<int> CrawlAsync(WebSession browser, string who, HashSet<string> reached, List<string> missing)
    {
        var queue = new Queue<(string Url, bool Htmx)>([("/", false)]);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/" };
        var variants = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pages = 0;

        void Follow(string target, bool htmx)
        {
            var path = target.Split('?')[0];
            if (Skip(path) || !seen.Add((htmx ? "htmx " : string.Empty) + target))
            {
                return;
            }

            var count = variants.GetValueOrDefault(path);
            if (target.Contains('?', StringComparison.Ordinal) && count >= MaxQueryVariantsPerPath)
            {
                return;
            }

            variants[path] = count + 1;
            queue.Enqueue((target, htmx));
        }

        while (queue.TryDequeue(out var next) && pages < MaxPagesPerRole)
        {
            // Links are followed as a browser follows them; hx-get partials (rows, modals) are fetched as htmx fetches them.
            var response = next.Htmx ? await browser.HtmxGetAsync(next.Url) : await browser.GetAsync(next.Url);
            if (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString is { } location && location.StartsWith('/'))
            {
                Follow(location, htmx: false);
            }

            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != "text/html")
            {
                continue;
            }

            pages++;
            var html = await response.Content.ReadAsStringAsync(Ct);
            foreach (var (action, token) in WebSession.PostForms(html))
            {
                reached.Add(Shape(action));
                if (token is null)
                {
                    missing.Add($"{who} {Shape(next.Url)} → form posting to {Shape(action)}");
                }
            }

            foreach (Match post in HtmxPostPattern().Matches(html))
            {
                reached.Add(Shape(WebUtility.HtmlDecode(post.Groups["url"].Value)));
            }

            foreach (Match link in LinkPattern().Matches(html))
            {
                Follow(WebUtility.HtmlDecode(link.Groups["url"].Value), link.Groups["kind"].Value == "hx-get");
            }
        }

        return pages;
    }

    /// <summary>Static files, downloads (documents, PDFs, exports) and sign-out are not pages to crawl.</summary>
    private static bool Skip(string path) =>
        path.StartsWith("//", StringComparison.Ordinal)
        || path.Contains('.', StringComparison.Ordinal)
        || path.Equals("/Account/Logout", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/Applications/Document/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/Applications/Contract/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/Applications/Schedule/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/Reports/Export", StringComparison.OrdinalIgnoreCase);

    /// <summary>A URL without its query and with ids replaced, so <c>/Applications/Approve/{id}</c> matches any application.</summary>
    private static string Shape(string url) => GuidPattern().Replace(url.Split('?')[0], "{id}").TrimEnd('/');

    /// <summary>Every POST action in the application, as the path its form posts to.</summary>
    private List<string> PostEndpoints()
    {
        var links = Factory.Services.GetRequiredService<LinkGenerator>();
        return Factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(a => a.ActionConstraints?.OfType<HttpMethodActionConstraint>().Any(c => c.HttpMethods.Contains("POST")) == true)
            .Select(a =>
            {
                var values = new RouteValueDictionary { ["area"] = a.RouteValues.TryGetValue("area", out var area) ? area ?? string.Empty : string.Empty };
                if (a.Parameters.Any(p => p.Name == "id"))
                {
                    values["id"] = Guid.Empty;
                }

                return links.GetPathByAction(a.ActionName, a.ControllerName, values)
                    ?? $"/{a.AttributeRouteInfo?.Template ?? $"{a.ControllerName}/{a.ActionName}"}";
            })
            .Select(Shape)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
