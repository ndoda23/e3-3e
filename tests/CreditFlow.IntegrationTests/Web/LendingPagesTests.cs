using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Products;
using CreditFlow.IntegrationTests.Infrastructure;
using CreditFlow.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>
/// The client, application and inbox pages through HTTP: an operator registers a client and submits an application
/// with documents; another branch gets 404 for every id; the Auditor's POST to any protected action is refused and
/// changes nothing.
/// </summary>
public sealed partial class LendingPagesTests(SqlServerContainer sql) : WebTestBase(sql)
{
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8.ToArray(), .. new byte[256]];

    [GeneratedRegex("/(?:Clients|Applications)/Details/(?<id>[0-9a-f-]{36})")]
    private static partial Regex DetailsLocation();

    [GeneratedRegex("/Applications/Document/[0-9a-f-]{36}\\?documentId=(?<id>[0-9a-f-]{36})")]
    private static partial Regex DocumentLink();

    private int _clientCounter;

    [Fact]
    public async Task An_operator_registers_a_client_and_submits_an_application_with_documents()
    {
        using var browser = await SignInAsync(RoleCodes.Operator, BranchSeedCatalog.Vake);

        var id = await CreateDraftAsync(browser);
        await UploadAsync(browser, id, DocumentType.IdentityDocumentCopy);
        await UploadAsync(browser, id, DocumentType.IncomeStatement);
        var draft = await browser.GetPageAsync($"/Applications/Details/{id}");
        Assert.Contains("data-document=\"IdentityDocumentCopy\"", draft, StringComparison.Ordinal);
        Assert.Contains("data-document=\"IncomeStatement\"", draft, StringComparison.Ordinal);
        Assert.DoesNotContain("data-missing", draft, StringComparison.Ordinal);

        var download = await browser.GetAsync(DocumentLink().Match(draft).Value.Replace("&amp;", "&", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync(Ct));

        var submitted = await PostAsync(browser, $"/Applications/Submit/{id}");
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);

        var page = await browser.GetPageAsync($"/Applications/Details/{id}");
        Assert.Contains("data-status=\"Submitted\"", page, StringComparison.Ordinal);
        Assert.Contains("data-history=\"Submitted\"", page, StringComparison.Ordinal);
        Assert.Contains($"data-application=\"{id}\"", await browser.GetPageAsync("/Applications"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_upload_of_the_wrong_type_is_refused_with_a_message()
    {
        using var browser = await SignInAsync(RoleCodes.Operator, BranchSeedCatalog.Vake);
        var id = await CreateDraftAsync(browser);

        var response = await UploadAsync(browser, id, DocumentType.IncomeStatement, "<html>not a pdf</html>"u8.ToArray());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var page = await browser.GetPageAsync($"/Applications/Details/{id}");
        Assert.Contains("data-error", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-document=", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_operator_of_another_branch_gets_404_for_every_id()
    {
        using var vake = await SignInAsync(RoleCodes.Operator, BranchSeedCatalog.Vake);
        var id = await CreateDraftAsync(vake);
        await UploadAsync(vake, id, DocumentType.IdentityDocumentCopy);
        var documentLink = DocumentLink().Match(await vake.GetPageAsync($"/Applications/Details/{id}")).Value.Replace("&amp;", "&", StringComparison.Ordinal);

        using var batumi = await SignInAsync(RoleCodes.Operator, BranchSeedCatalog.Batumi);

        Assert.DoesNotContain(id.ToString(), await batumi.GetPageAsync("/Applications"), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await batumi.GetAsync($"/Applications/Details/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await batumi.GetAsync(documentLink)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(batumi, $"/Applications/Cancel/{id}", new() { ["reason"] = "Not mine." })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(batumi, $"/Applications/Submit/{id}")).StatusCode);
        Assert.Equal(ApplicationStatus.Draft, await StatusAsync(id));
    }

    [Fact]
    public async Task Inboxes_are_shown_by_permission()
    {
        using var underwriter = await SignInAsync(RoleCodes.Underwriter);
        Assert.Contains("data-current-inbox=\"Underwriting\"", await underwriter.GetPageAsync("/Inbox"), StringComparison.Ordinal);

        using var compliance = await SignInAsync(RoleCodes.Compliance);
        Assert.Contains("data-current-inbox=\"Compliance\"", await compliance.GetPageAsync("/Inbox?inbox=Underwriting"), StringComparison.Ordinal);

        using var operatorSession = await SignInAsync(RoleCodes.Operator, BranchSeedCatalog.Vake);
        Assert.Equal("/Account/AccessDenied", (await operatorSession.GetAsync("/Inbox")).Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task The_auditor_cannot_post_to_any_protected_action()
    {
        using var operatorSession = await SignInAsync(RoleCodes.Operator, BranchSeedCatalog.Vake);
        var applicationId = await CreateDraftAsync(operatorSession);
        await UploadAsync(operatorSession, applicationId, DocumentType.IdentityDocumentCopy);

        var (userName, auditorId, password) = await CreateUserAsync(RoleCodes.Auditor);
        using var auditor = NewSession();
        await auditor.LoginAsync(userName, password);
        var token = WebSession.Token(await auditor.GetPageAsync("/"));
        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync($"/Applications/Details/{applicationId}")).StatusCode);
        var auditBefore = await QueryAsync(db => db.AuditLogs.CountAsync(a => a.UserId == auditorId.Value, Ct));

        var protectedPosts = PostActions().Where(a => a.IsProtected).ToList();
        Assert.NotEmpty(protectedPosts);
        var allowed = new List<string>();
        foreach (var action in protectedPosts)
        {
            var response = await auditor.HtmxPostAsync(action.Path(applicationId), new Dictionary<string, string>(), token);
            if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.NotFound))
            {
                allowed.Add($"{action.Path(applicationId)} → {(int)response.StatusCode}");
            }
        }

        Assert.Empty(allowed);
        Assert.Equal(auditBefore, await QueryAsync(db => db.AuditLogs.CountAsync(a => a.UserId == auditorId.Value, Ct)));
        Assert.Equal(ApplicationStatus.Draft, await StatusAsync(applicationId));
        Assert.Equal(1, await QueryAsync(db => db.LoanApplications.Where(a => a.Id == new LoanApplicationId(applicationId))
            .SelectMany(a => a.Documents).CountAsync(Ct)));
    }

    [Fact]
    public void Every_post_action_without_a_permission_is_account_self_service()
    {
        var unprotected = PostActions().Where(a => !a.IsProtected).Select(a => a.Controller.Name).Distinct().ToList();

        Assert.All(unprotected, name => Assert.Contains(name, new[] { nameof(AccountController), nameof(ManageController) }));
    }

    // ----- Helpers -----

    private sealed record PostAction(Type Controller, MethodInfo Method, bool IsProtected)
    {
        public string Path(Guid id)
        {
            var template = Method.GetCustomAttribute<HttpPostAttribute>()!.Template;
            if (template is not null)
            {
                return template;
            }

            var area = Controller.GetCustomAttribute<AreaAttribute>(inherit: true)?.RouteValue;
            var controller = Controller.Name[..^"Controller".Length];
            return $"{(area is null ? null : "/" + area)}/{controller}/{Method.Name}/{id}";
        }
    }

    /// <summary>Every POST action of the web application; protected = a permission or administration policy applies.</summary>
    private static IEnumerable<PostAction> PostActions() =>
        typeof(DashboardController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<HttpPostAttribute>() is not null)
                .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is null)
                .Select(m => new PostAction(t, m,
                    t.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Concat(m.GetCustomAttributes<AuthorizeAttribute>())
                        .Any(a => a.Policy is not null))));

    private async Task<WebSession> SignInAsync(string roleCode, string? branchCode = null)
    {
        var (userName, _, password) = await CreateUserAsync(roleCode, branchCode);
        var browser = NewSession();
        Assert.Equal(HttpStatusCode.Redirect, (await browser.LoginAsync(userName, password)).StatusCode);
        return browser;
    }

    private static async Task<HttpResponseMessage> PostAsync(WebSession browser, string path, Dictionary<string, string>? fields = null) =>
        await browser.PostWithTokenAsync(path, fields ?? [], WebSession.Token(await browser.GetPageAsync("/")));

    private static Guid IdFrom(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(DetailsLocation().Match(response.Headers.Location!.OriginalString).Groups["id"].Value);
    }

    /// <summary>Registers a client with a salary and creates a consumer-loan draft for them, all through the pages.</summary>
    private async Task<Guid> CreateDraftAsync(WebSession browser)
    {
        var personalNumber = $"0300101{1000 + Interlocked.Increment(ref _clientCounter)}";
        var clientId = IdFrom(await browser.PostFormAsync("/Clients/Register", "/Clients/Register", new Dictionary<string, string>
        {
            ["PersonalNumber"] = personalNumber,
            ["FirstName"] = "Eka",
            ["LastName"] = "Tsiklauri",
            ["BirthDate"] = "1988-04-02",
            ["Phone"] = "+995 555 123456",
            ["Address"] = "Tbilisi, Chavchavadze 1",
            ["DocumentType"] = "IdCard",
            ["DocumentNumber"] = "ID" + personalNumber,
            ["DocumentIssuedOn"] = "2021-01-01",
            ["DocumentExpiresOn"] = "2031-01-01",
        }));
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(browser, $"/Clients/AddIncome/{clientId}", new()
        {
            ["Type"] = "Salary",
            ["Source"] = "Employer LLC",
            ["MonthlyAmount"] = "3000",
            ["Currency"] = "GEL",
        })).StatusCode);
        var client = await browser.GetPageAsync($"/Clients/Details/{clientId}");
        Assert.Contains("data-income", client, StringComparison.Ordinal);

        var product = await QueryAsync(db => db.LoanProducts.Where(p => p.Code == LendingSeedCatalog.Consumer).Select(p => p.Id).SingleAsync(Ct));
        return IdFrom(await browser.PostFormAsync($"/Applications/New?personalNumber={personalNumber}", "/Applications/New", new Dictionary<string, string>
        {
            ["BorrowerPersonalNumber"] = personalNumber,
            ["ProductId"] = product.Value.ToString(),
            ["Amount"] = "5000",
            ["Currency"] = "GEL",
            ["TermMonths"] = "24",
            ["AnnualRate"] = "18",
            ["ScheduleType"] = "Annuity",
        }));
    }

    private static async Task<HttpResponseMessage> UploadAsync(WebSession browser, Guid id, DocumentType type, byte[]? content = null)
    {
        var token = WebSession.Token(await browser.GetPageAsync($"/Applications/Details/{id}"));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        form.Add(new StringContent(type.ToString()), "type");
        var file = new ByteArrayContent(content ?? Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", $"{type}.pdf");
        var response = await browser.Client.PostAsync($"/Applications/Upload/{id}", form, Ct);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response;
    }

    private Task<ApplicationStatus> StatusAsync(Guid id) =>
        QueryAsync(db => db.LoanApplications.Where(a => a.Id == new LoanApplicationId(id)).Select(a => a.Status).SingleAsync(Ct));
}
