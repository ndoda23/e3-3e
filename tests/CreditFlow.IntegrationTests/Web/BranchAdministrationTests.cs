using System.Net;
using System.Text.RegularExpressions;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Users;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Admin › Branches: create, edit (immutable code), deactivate instead of delete, delete only when unused.</summary>
public sealed class BranchAdministrationTests(SqlServerContainer sql) : WebTestBase(sql)
{
    [Fact]
    public async Task List_shows_the_seeded_branches()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;

        var html = await browser.GetPageAsync("/Admin/Branches");

        Assert.Equal(6, Regex.Matches(html, "data-branch=\"").Count);
        Assert.Contains($"data-branch=\"{BranchSeedCatalog.Vake}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_branch_gets_an_upper_case_code_and_is_audited()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;

        var response = await CreateBranchAsync(browser, "gld", "Gldani", "Tbilisi");

        Assert.Equal("/Admin/Branches", response.Headers.Location!.OriginalString);
        var branch = await QueryAsync(db => db.Branches.SingleAsync(b => b.Code == "GLD", Ct));
        Assert.True(branch.IsActive);
        Assert.True(await QueryAsync(db => db.AuditLogs.AnyAsync(
            l => l.EntityType == "Branch" && l.Action == "Created" && l.EntityId == branch.Id.ToString(), Ct)));
    }

    [Fact]
    public async Task A_duplicate_code_is_rejected_next_to_the_code_field()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;

        var response = await CreateBranchAsync(browser, BranchSeedCatalog.Vake.ToLowerInvariant(), "Another Vake", "Tbilisi");
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("data-valmsg-for=\"Code\"[^>]*>[^<]+", html);
        Assert.False(await QueryAsync(db => db.Branches.AnyAsync(b => b.Name == "Another Vake", Ct)));
    }

    [Fact]
    public async Task Editing_changes_name_and_city_but_never_the_code()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var id = await BranchIdAsync(BranchSeedCatalog.Batumi);

        var response = await browser.PostFormAsync($"/Admin/Branches/Edit/{id}", $"/Admin/Branches/Edit/{id}", new Dictionary<string, string>
        {
            ["Code"] = "NEW",
            ["Name"] = "Batumi Boulevard",
            ["City"] = "Batumi",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var branch = await QueryAsync(db => db.Branches.SingleAsync(b => b.Id == new BranchId(id), Ct));
        Assert.Equal(BranchSeedCatalog.Batumi, branch.Code);
        Assert.Equal("Batumi Boulevard", branch.Name);
    }

    [Fact]
    public async Task A_deactivated_branch_is_no_longer_offered_but_its_users_keep_it()
    {
        var (_, userId, _) = await CreateUserAsync(RoleCodes.Operator, BranchSeedCatalog.Kutaisi);
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var id = await BranchIdAsync(BranchSeedCatalog.Kutaisi);
        var operatorRole = await QueryAsync(db => db.Roles.Where(r => r.Code == RoleCodes.Operator).Select(r => r.Id).SingleAsync(Ct));

        var modal = await (await browser.HtmxGetAsync($"/Admin/Branches/Confirm/{id}?delete=false")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("1 user(s)", modal, StringComparison.Ordinal);
        await browser.PostWithTokenAsync($"/Admin/Branches/Deactivate/{id}", new Dictionary<string, string>(), WebSession.Token(modal));

        Assert.False(await QueryAsync(db => db.Branches.Where(b => b.Id == new BranchId(id)).Select(b => b.IsActive).SingleAsync(Ct)));
        var newUserFields = await (await browser.HtmxGetAsync($"/Admin/Users/RoleFields?RoleId={operatorRole}")).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(id.ToString(), newUserFields, StringComparison.Ordinal);
        var editExisting = await browser.GetPageAsync($"/Admin/Users/Edit/{userId}");
        Assert.Contains($"value=\"{id}\"", editExisting, StringComparison.Ordinal);
        Assert.Equal(id, (await QueryAsync(db => db.StaffMembers.Where(s => s.Id == userId).Select(s => s.BranchId).SingleAsync(Ct)))!.Value.Value);
    }

    [Fact]
    public async Task Only_an_unused_branch_can_be_deleted()
    {
        await CreateUserAsync(RoleCodes.Operator, BranchSeedCatalog.Rustavi);
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        await CreateBranchAsync(browser, "TMP", "Temporary", "Tbilisi");
        var used = await BranchIdAsync(BranchSeedCatalog.Rustavi);
        var unused = await BranchIdAsync("TMP");

        var list = await browser.GetPageAsync("/Admin/Branches");
        Assert.DoesNotContain($"Confirm/{used}?delete=true", list, StringComparison.Ordinal);
        Assert.Contains($"Confirm/{unused}?delete=true", list, StringComparison.Ordinal);

        var token = WebSession.Token(list);
        await browser.PostWithTokenAsync($"/Admin/Branches/Delete/{used}", new Dictionary<string, string>(), token);
        await browser.PostWithTokenAsync($"/Admin/Branches/Delete/{unused}", new Dictionary<string, string>(), token);

        Assert.True(await QueryAsync(db => db.Branches.AnyAsync(b => b.Id == new BranchId(used), Ct)));
        Assert.False(await QueryAsync(db => db.Branches.AnyAsync(b => b.Id == new BranchId(unused), Ct)));
    }

    [Fact]
    public async Task Business_users_cannot_open_branches()
    {
        var (userName, _, password) = await CreateOperatorAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var response = await browser.GetAsync("/Admin/Branches");

        Assert.Equal("/Account/AccessDenied", response.Headers.Location!.AbsolutePath);
    }

    private static Task<HttpResponseMessage> CreateBranchAsync(WebSession browser, string code, string name, string city) =>
        browser.PostFormAsync("/Admin/Branches/Create", "/Admin/Branches/Create", new Dictionary<string, string>
        {
            ["Code"] = code,
            ["Name"] = name,
            ["City"] = city,
        });

    private async Task<Guid> BranchIdAsync(string code) =>
        (await QueryAsync(db => db.Branches.Where(b => b.Code == code).Select(b => b.Id).SingleAsync(Ct))).Value;
}
