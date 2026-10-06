using System.Net;
using System.Text.RegularExpressions;
using Lams.Application.ReferenceData;
using Lams.Domain.Auditing;
using Lams.Domain.Users;
using Lams.Infrastructure.Identity;
using Lams.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lams.IntegrationTests.Web;

/// <summary>Admin › Users against the real app: list, create / edit, user actions and the guard rails.</summary>
public sealed partial class UserAdministrationTests(SqlServerContainer sql) : WebTestBase(sql)
{
    [GeneratedRegex("data-temp-password>(?<password>[^<]+)<")]
    private static partial Regex TemporaryPasswordPattern();

    // ----- List -----

    [Fact]
    public async Task Administrator_sees_the_user_list_and_htmx_filters_return_only_the_rows()
    {
        var (underwriter, _, _) = await CreateSeniorUnderwriterAsync();
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;

        var page = await browser.GetPageAsync("/Admin/Users");
        Assert.Contains($"data-user=\"{underwriter}\"", page, StringComparison.Ordinal);
        Assert.Contains("<html", page, StringComparison.Ordinal);

        var rows = await browser.HtmxGetAsync($"/Admin/Users/Rows?search={underwriter}");
        var html = await rows.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, rows.StatusCode);
        Assert.DoesNotContain("<html", html, StringComparison.Ordinal);
        Assert.Contains($"data-user=\"{underwriter}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-user=\"{InfrastructureHost.AdminUserName}\"", html, StringComparison.Ordinal);

        // The address bar follows the filters (bookmarkable), pointing at the full page.
        // ("/Admin" is the Users list: Users is the area's default controller.)
        Assert.Equal($"/Admin?search={underwriter}", rows.Headers.GetValues("HX-Push-Url").Single());
    }

    [Fact]
    public async Task List_rows_without_htmx_redirect_to_the_full_page()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;

        var response = await browser.GetAsync("/Admin/Users/Rows?status=Inactive");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin?status=Inactive", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Status_filter_and_sort_are_applied()
    {
        var (_, inactiveId, _) = await CreateUserAsync(RoleCodes.Director);
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        await PostActionAsync(browser, inactiveId, "Deactivate");

        var html = await (await browser.HtmxGetAsync("/Admin/Users/Rows?status=Inactive&sort=LastLogin&desc=true"))
            .Content.ReadAsStringAsync(Ct);

        Assert.Single(Regex.Matches(html, "data-user=\""));
        Assert.Contains("aria-sort=\"descending\"", html, StringComparison.Ordinal);
    }

    // ----- Create -----

    [Fact]
    public async Task Creating_a_senior_underwriter_shows_the_temporary_password_once_and_it_signs_in()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var (uwRole, vake) = await QueryAsync(async db => (
            await db.Roles.Where(r => r.Code == RoleCodes.Underwriter).Select(r => r.Id).SingleAsync(Ct),
            (await db.Branches.Where(b => b.Code == BranchSeedCatalog.Vake).Select(b => b.Id).SingleAsync(Ct)).Value));

        var response = await browser.PostFormAsync("/Admin/Users/Create", "/Admin/Users/Create", new Dictionary<string, string>
        {
            ["FullName"] = "Nino Senior",
            ["UserName"] = "nino.senior",
            ["Email"] = "nino.s@pcb.ge",
            ["RoleId"] = uwRole.ToString(),
            ["BranchId"] = vake.ToString(),
            ["AuthorityLevel"] = "Senior",
            ["Capacity"] = "12",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        // Generated passwords contain symbols, which Razor HTML-encodes.
        var password = WebUtility.HtmlDecode(
            TemporaryPasswordPattern().Match(await response.Content.ReadAsStringAsync(Ct)).Groups["password"].Value);
        Assert.False(string.IsNullOrWhiteSpace(password));

        var staff = await QueryAsync(db => db.StaffMembers.SingleAsync(s => s.FullName == "Nino Senior", Ct));
        Assert.Equal(AuthorityLevel.Senior, staff.AuthorityLevel);
        Assert.Equal(vake, staff.BranchId!.Value.Value);
        Assert.Equal(12, staff.Capacity);

        using var newUser = NewSession();
        var login = await newUser.LoginAsync("nino.senior", password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/Account/ChangePassword", (await newUser.GetAsync("/")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Create_shows_every_field_error_at_once()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var uwRole = await QueryAsync(db => db.Roles.Where(r => r.Code == RoleCodes.Underwriter).Select(r => r.Id).SingleAsync(Ct));

        var response = await browser.PostFormAsync("/Admin/Users/Create", "/Admin/Users/Create", new Dictionary<string, string>
        {
            ["FullName"] = "Missing Attributes",
            ["UserName"] = "x",
            ["Email"] = "not-an-email",
            ["RoleId"] = uwRole.ToString(),
            ["Capacity"] = "10",
        });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The selected role requires a branch.", html, StringComparison.Ordinal);
        Assert.Contains("The selected role requires an authority level.", html, StringComparison.Ordinal);
        Assert.Contains("A user name has 3 to 64", html, StringComparison.Ordinal);
        Assert.Contains("Enter a valid email address.", html, StringComparison.Ordinal);
        Assert.False(await QueryAsync(db => db.StaffMembers.AnyAsync(s => s.FullName == "Missing Attributes", Ct)));
    }

    [Theory]
    [InlineData(RoleCodes.Underwriter, true, true, false)]
    [InlineData(RoleCodes.Head, true, false, true)]
    [InlineData(RoleCodes.SystemAdministrator, false, false, false)]
    public async Task Role_fields_follow_the_role(string roleCode, bool branch, bool level, bool segment)
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var roleId = await QueryAsync(db => db.Roles.Where(r => r.Code == roleCode).Select(r => r.Id).SingleAsync(Ct));

        var html = await (await browser.HtmxGetAsync($"/Admin/Users/RoleFields?RoleId={roleId}")).Content.ReadAsStringAsync(Ct);

        Assert.Contains($"data-role=\"{roleCode}\"", html, StringComparison.Ordinal);
        Assert.Equal(branch, html.Contains("name=\"BranchId\"", StringComparison.Ordinal));
        Assert.Equal(level, html.Contains("name=\"AuthorityLevel\"", StringComparison.Ordinal));
        Assert.Equal(segment, html.Contains("name=\"Segment\"", StringComparison.Ordinal));
    }

    // ----- Edit -----

    [Fact]
    public async Task Changing_the_branch_saves_it_and_ends_the_users_sessions()
    {
        var (userName, id, _) = await CreateSeniorUnderwriterAsync();
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var (uwRole, saburtalo, stampBefore) = await QueryAsync(async db => (
            await db.Roles.Where(r => r.Code == RoleCodes.Underwriter).Select(r => r.Id).SingleAsync(Ct),
            (await db.Branches.Where(b => b.Code == BranchSeedCatalog.Saburtalo).Select(b => b.Id).SingleAsync(Ct)).Value,
            await db.Users.Where(u => u.Id == id.Value).Select(u => u.SecurityStamp).SingleAsync(Ct)));

        var response = await browser.PostFormAsync($"/Admin/Users/Edit/{id}", $"/Admin/Users/Edit/{id}", new Dictionary<string, string>
        {
            ["FullName"] = "Test UW",
            ["UserName"] = userName,
            ["Email"] = $"{userName}@pcb.ge",
            ["RoleId"] = uwRole.ToString(),
            ["BranchId"] = saburtalo.ToString(),
            ["AuthorityLevel"] = "Senior",
            ["Capacity"] = "10",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Users/Details/{id}", response.Headers.Location!.OriginalString);
        var (branchId, stampAfter) = await QueryAsync(async db => (
            await db.StaffMembers.Where(s => s.Id == id).Select(s => s.BranchId).SingleAsync(Ct),
            await db.Users.Where(u => u.Id == id.Value).Select(u => u.SecurityStamp).SingleAsync(Ct)));
        Assert.Equal(saburtalo, branchId!.Value.Value);
        Assert.NotEqual(stampBefore, stampAfter);
        Assert.Contains("The user has been saved.", await browser.GetPageAsync($"/Admin/Users/Details/{id}"), StringComparison.Ordinal);
    }

    // ----- Actions -----

    [Fact]
    public async Task Deactivation_is_confirmed_in_a_modal_and_audited()
    {
        var (_, id, _) = await CreateSeniorUnderwriterAsync();
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;

        var modal = await (await browser.HtmxGetAsync($"/Admin/Users/Confirm/{id}?operation=Deactivate")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("role=\"dialog\"", modal, StringComparison.Ordinal);
        Assert.Contains($"action=\"/Admin/Users/Deactivate/{id}\"", modal, StringComparison.Ordinal);

        var response = await browser.PostWithTokenAsync($"/Admin/Users/Deactivate/{id}", new Dictionary<string, string>(), WebSession.Token(modal));

        Assert.Equal($"/Admin/Users/Details/{id}", response.Headers.Location!.OriginalString);
        var (active, audited) = await QueryAsync(async db => (
            await db.StaffMembers.Where(s => s.Id == id).Select(s => s.IsActive).SingleAsync(Ct),
            await db.AuditLogs.AnyAsync(l => l.Action == AuditActions.UserDeactivated && l.EntityId == id.ToString(), Ct)));
        Assert.False(active);
        Assert.True(audited);
    }

    [Fact]
    public async Task Administrators_cannot_deactivate_themselves()
    {
        var (browser, adminId) = await SignInAsAdministratorAsync();
        using var _browser = browser;

        var details = await browser.GetPageAsync($"/Admin/Users/Details/{adminId}");
        Assert.DoesNotContain("operation=Deactivate", details, StringComparison.Ordinal);

        await PostActionAsync(browser, adminId, "Deactivate");

        Assert.Contains("You cannot deactivate your own account.", await browser.GetPageAsync($"/Admin/Users/Details/{adminId}"), StringComparison.Ordinal);
        Assert.True(await QueryAsync(db => db.StaffMembers.Where(s => s.Id == adminId).Select(s => s.IsActive).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Password_reset_shows_the_new_password_once_in_a_modal()
    {
        var (_, id, _) = await CreateSeniorUnderwriterAsync();
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var token = WebSession.Token(await browser.GetPageAsync($"/Admin/Users/Details/{id}"));

        var response = await browser.HtmxPostAsync($"/Admin/Users/ResetPassword/{id}", new Dictionary<string, string>(), token);
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains("role=\"dialog\"", html, StringComparison.Ordinal);
        var password = TemporaryPasswordPattern().Match(html).Groups["password"].Value;
        Assert.False(string.IsNullOrWhiteSpace(password));
        Assert.True(await QueryAsync(db => db.Users.Where(u => u.Id == id.Value).Select(u => u.MustChangePassword).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Unlock_force_sign_out_and_mfa_requirement_are_applied()
    {
        var (_, id, _) = await CreateSeniorUnderwriterAsync();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await userManager.FindByIdAsync(id.ToString()))!;
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddDays(1));
        }

        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var stampBefore = await QueryAsync(db => db.Users.Where(u => u.Id == id.Value).Select(u => u.SecurityStamp).SingleAsync(Ct));

        await PostActionAsync(browser, id, "Unlock");
        await PostActionAsync(browser, id, "ForceSignOut");
        await PostActionAsync(browser, id, "RequireMfa", new Dictionary<string, string> { ["require"] = "true" });

        var account = await QueryAsync(db => db.Users.SingleAsync(u => u.Id == id.Value, Ct));
        Assert.Null(account.LockoutEnd);
        Assert.NotEqual(stampBefore, account.SecurityStamp);
        Assert.True(account.RequireMfa);
        var actions = await QueryAsync(db => db.AuditLogs.Where(l => l.EntityId == id.ToString()).Select(l => l.Action).ToListAsync(Ct));
        Assert.Contains(AuditActions.UserUnlocked, actions);
        Assert.Contains(AuditActions.ForcedSignOut, actions);
        Assert.Contains(AuditActions.MfaRequirementChanged, actions);
    }

    [Fact]
    public async Task Availability_switch_updates_itself_with_htmx_and_shows_a_toast()
    {
        var (_, id, _) = await CreateSeniorUnderwriterAsync();
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var token = WebSession.Token(await browser.GetPageAsync("/Admin/Users"));

        var response = await browser.HtmxPostAsync(
            $"/Admin/Users/Availability/{id}", new Dictionary<string, string> { ["isAvailable"] = "false" }, token);
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("aria-checked=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("showToast", response.Headers.GetValues("HX-Trigger").Single(), StringComparison.Ordinal);
        Assert.False(await QueryAsync(db => db.StaffMembers.Where(s => s.Id == id).Select(s => s.IsAvailable).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Non_administrators_cannot_use_user_actions()
    {
        var (_, targetId, _) = await CreateUserAsync(RoleCodes.LoanOfficer, BranchSeedCatalog.Vake, AuthorityLevel.Junior);
        var (userName, _, password) = await CreateSeniorUnderwriterAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);
        var token = WebSession.Token(await browser.GetPageAsync("/"));

        var response = await browser.PostWithTokenAsync($"/Admin/Users/Deactivate/{targetId}", new Dictionary<string, string>(), token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location!.AbsolutePath);
        Assert.True(await QueryAsync(db => db.StaffMembers.Where(s => s.Id == targetId).Select(s => s.IsActive).SingleAsync(Ct)));
    }

    // ----- Dashboard and user card (UI fixes) -----

    [Fact]
    public async Task Underwriter_dashboard_shows_level_chip_and_actual_authority_limit()
    {
        var (userName, _, password) = await CreateSeniorUnderwriterAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var html = WebUtility.HtmlDecode(await browser.GetPageAsync("/"));

        Assert.Contains("data-dashboard-chip>Sr UW · Test UW<", html, StringComparison.Ordinal);
        Assert.Contains("₾50,000", html, StringComparison.Ordinal);
        Assert.Contains("data-user-role>Senior Underwriter<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Branch_dashboard_shows_the_branch_limit_and_officers_see_none()
    {
        var (manager, _, password) = await CreateUserAsync(RoleCodes.BranchManager, BranchSeedCatalog.Vake);
        var (officer, _, officerPassword) = await CreateUserAsync(RoleCodes.LoanOfficer, BranchSeedCatalog.Vake, AuthorityLevel.Junior);
        using var managerBrowser = NewSession();
        using var officerBrowser = NewSession();
        await managerBrowser.LoginAsync(manager, password);
        await officerBrowser.LoginAsync(officer, officerPassword);

        var managerHtml = WebUtility.HtmlDecode(await managerBrowser.GetPageAsync("/"));
        var officerHtml = await officerBrowser.GetPageAsync("/");

        Assert.Contains("data-dashboard-chip>BM · Test BM<", managerHtml, StringComparison.Ordinal);
        Assert.Contains("₾150,000", managerHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("data-authority-limit", officerHtml, StringComparison.Ordinal);
    }

    private static async Task PostActionAsync(
        WebSession browser, UserId id, string action, IDictionary<string, string>? fields = null)
    {
        var token = WebSession.Token(await browser.GetPageAsync($"/Admin/Users/Details/{id}"));
        var response = await browser.PostWithTokenAsync($"/Admin/Users/{action}/{id}", fields ?? new Dictionary<string, string>(), token);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
