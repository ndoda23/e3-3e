using System.Net;
using System.Text.RegularExpressions;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Admin › Users against the real app: list, create / edit, user actions and the guard rails.</summary>
public sealed partial class UserAdministrationTests(SqlServerContainer sql) : WebTestBase(sql)
{
    [GeneratedRegex("data-temp-password>(?<password>[^<]+)<")]
    private static partial Regex TemporaryPasswordPattern();

    // ----- List -----

    [Fact]
    public async Task Administrator_sees_the_user_list_and_htmx_filters_return_only_the_rows()
    {
        var (underwriter, _, _) = await CreateOperatorAsync();
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
        var (_, inactiveId, _) = await CreateUserAsync(RoleCodes.Auditor);
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var modal = await (await browser.HtmxGetAsync($"/Admin/Users/Confirm/{inactiveId}?operation=Deactivate")).Content.ReadAsStringAsync(Ct);
        await browser.SubmitFormAsync(modal, $"/Admin/Users/Deactivate/{inactiveId}", new Dictionary<string, string>());

        var html = await (await browser.HtmxGetAsync("/Admin/Users/Rows?status=Inactive&sort=LastLogin&desc=true"))
            .Content.ReadAsStringAsync(Ct);

        Assert.Single(Regex.Matches(html, "data-user=\""));
        Assert.Contains("aria-sort=\"descending\"", html, StringComparison.Ordinal);
    }

    // ----- Create -----

    [Fact]
    public async Task Creating_an_operator_shows_the_temporary_password_once_and_it_signs_in()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var (operatorRole, vake) = await QueryAsync(async db => (
            await db.Roles.Where(r => r.Code == RoleCodes.Operator).Select(r => r.Id).SingleAsync(Ct),
            (await db.Branches.Where(b => b.Code == BranchSeedCatalog.Vake).Select(b => b.Id).SingleAsync(Ct)).Value));

        var response = await browser.PostFormAsync("/Admin/Users/Create", "/Admin/Users/Create", new Dictionary<string, string>
        {
            ["FullName"] = "Nino Operator",
            ["UserName"] = "nino.operator",
            ["Email"] = "nino.o@pcb.ge",
            ["RoleId"] = operatorRole.ToString(),
            ["BranchId"] = vake.ToString(),
            ["Capacity"] = "12",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        // Generated passwords contain symbols, which Razor HTML-encodes.
        var password = WebUtility.HtmlDecode(
            TemporaryPasswordPattern().Match(await response.Content.ReadAsStringAsync(Ct)).Groups["password"].Value);
        Assert.False(string.IsNullOrWhiteSpace(password));

        var staff = await QueryAsync(db => db.StaffMembers.SingleAsync(s => s.FullName == "Nino Operator", Ct));
        Assert.Equal(vake, staff.BranchId!.Value.Value);
        Assert.Equal(12, staff.Capacity);

        using var newUser = NewSession();
        var login = await newUser.LoginAsync("nino.operator", password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/Account/ChangePassword", (await newUser.GetAsync("/")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Create_shows_every_field_error_at_once()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var operatorRole = await QueryAsync(db => db.Roles.Where(r => r.Code == RoleCodes.Operator).Select(r => r.Id).SingleAsync(Ct));

        var response = await browser.PostFormAsync("/Admin/Users/Create", "/Admin/Users/Create", new Dictionary<string, string>
        {
            ["FullName"] = "Missing Attributes",
            ["UserName"] = "x",
            ["Email"] = "not-an-email",
            ["RoleId"] = operatorRole.ToString(),
            ["Capacity"] = "10",
        });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The selected role requires a branch.", html, StringComparison.Ordinal);
        Assert.Contains("A user name has 3 to 64", html, StringComparison.Ordinal);
        Assert.Contains("Enter a valid email address.", html, StringComparison.Ordinal);
        Assert.False(await QueryAsync(db => db.StaffMembers.AnyAsync(s => s.FullName == "Missing Attributes", Ct)));
    }

    [Theory]
    [InlineData(RoleCodes.Operator, true)]
    [InlineData(RoleCodes.Underwriter, false)]
    [InlineData(RoleCodes.Admin, false)]
    public async Task Branch_field_follows_the_role(string roleCode, bool branch)
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var roleId = await QueryAsync(db => db.Roles.Where(r => r.Code == roleCode).Select(r => r.Id).SingleAsync(Ct));

        var html = await (await browser.HtmxGetAsync($"/Admin/Users/RoleFields?RoleId={roleId}")).Content.ReadAsStringAsync(Ct);

        Assert.Contains($"data-role=\"{roleCode}\"", html, StringComparison.Ordinal);
        Assert.Equal(branch, html.Contains("name=\"BranchId\"", StringComparison.Ordinal));
    }

    // ----- Edit -----

    [Fact]
    public async Task Changing_the_branch_saves_it_and_ends_the_users_sessions()
    {
        var (userName, id, _) = await CreateOperatorAsync();
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var (operatorRole, saburtalo, stampBefore) = await QueryAsync(async db => (
            await db.Roles.Where(r => r.Code == RoleCodes.Operator).Select(r => r.Id).SingleAsync(Ct),
            (await db.Branches.Where(b => b.Code == BranchSeedCatalog.Saburtalo).Select(b => b.Id).SingleAsync(Ct)).Value,
            await db.Users.Where(u => u.Id == id.Value).Select(u => u.SecurityStamp).SingleAsync(Ct)));

        var response = await browser.PostFormAsync($"/Admin/Users/Edit/{id}", $"/Admin/Users/Edit/{id}", new Dictionary<string, string>
        {
            ["FullName"] = "Test OP",
            ["UserName"] = userName,
            ["Email"] = $"{userName}@pcb.ge",
            ["RoleId"] = operatorRole.ToString(),
            ["BranchId"] = saburtalo.ToString(),
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
        var (_, id, _) = await CreateOperatorAsync();
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;

        var modal = await (await browser.HtmxGetAsync($"/Admin/Users/Confirm/{id}?operation=Deactivate")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("role=\"dialog\"", modal, StringComparison.Ordinal);
        Assert.Contains($"action=\"/Admin/Users/Deactivate/{id}\"", modal, StringComparison.Ordinal);

        var response = await browser.SubmitFormAsync(modal, $"/Admin/Users/Deactivate/{id}", new Dictionary<string, string>());

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

        Assert.Equal(HttpStatusCode.Redirect, (await browser.PostWithSessionTokenAsync($"/Admin/Users/Deactivate/{adminId}", new Dictionary<string, string>())).StatusCode);

        Assert.Contains("You cannot deactivate your own account.", await browser.GetPageAsync($"/Admin/Users/Details/{adminId}"), StringComparison.Ordinal);
        Assert.True(await QueryAsync(db => db.StaffMembers.Where(s => s.Id == adminId).Select(s => s.IsActive).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Password_reset_shows_the_new_password_once_in_a_modal()
    {
        var (_, id, _) = await CreateOperatorAsync();
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var details = await browser.GetPageAsync($"/Admin/Users/Details/{id}");
        var modal = await (await browser.HtmxGetAsync($"/Admin/Users/Confirm/{id}?operation=ResetPassword")).Content.ReadAsStringAsync(Ct);
        Assert.Contains($"hx-post=\"/Admin/Users/ResetPassword/{id}\"", modal, StringComparison.Ordinal);

        var response = await browser.HtmxPostAsync($"/Admin/Users/ResetPassword/{id}", new Dictionary<string, string>(), details);
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
        var (_, id, _) = await CreateOperatorAsync();
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
        var forceSignOut = await (await browser.HtmxGetAsync($"/Admin/Users/Confirm/{id}?operation=ForceSignOut")).Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SubmitFormAsync(forceSignOut, $"/Admin/Users/ForceSignOut/{id}", new Dictionary<string, string>())).StatusCode);
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
        var (_, id, _) = await CreateOperatorAsync();
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var list = await browser.GetPageAsync("/Admin/Users");
        Assert.Contains($"hx-post=\"/Admin/Users/Availability/{id}\"", list, StringComparison.Ordinal);

        var response = await browser.HtmxPostAsync(
            $"/Admin/Users/Availability/{id}", new Dictionary<string, string> { ["isAvailable"] = "false" }, list);
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("aria-checked=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("showToast", response.Headers.GetValues("HX-Trigger").Single(), StringComparison.Ordinal);
        Assert.False(await QueryAsync(db => db.StaffMembers.Where(s => s.Id == id).Select(s => s.IsAvailable).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Non_administrators_cannot_use_user_actions()
    {
        var (_, targetId, _) = await CreateUserAsync(RoleCodes.Operator, BranchSeedCatalog.Vake);
        var (userName, _, password) = await CreateOperatorAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);
        var response = await browser.PostWithSessionTokenAsync($"/Admin/Users/Deactivate/{targetId}", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location!.AbsolutePath);
        Assert.True(await QueryAsync(db => db.StaffMembers.Where(s => s.Id == targetId).Select(s => s.IsActive).SingleAsync(Ct)));
    }

    // ----- Dashboard and user card -----

    [Fact]
    public async Task Operator_dashboard_shows_the_role_chip_and_the_branch_on_the_user_card()
    {
        var (userName, _, password) = await CreateOperatorAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var html = WebUtility.HtmlDecode(await browser.GetPageAsync("/"));

        Assert.Contains("data-dashboard-chip>OP · Test OP<", html, StringComparison.Ordinal);
        Assert.Contains("data-dashboard=\"Operator\"", html, StringComparison.Ordinal);
        Assert.Contains("data-user-role>Operator<", html, StringComparison.Ordinal);
        Assert.Contains(">Vake<", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"brand-name\">CreditFlow</div>", html, StringComparison.Ordinal);
        Assert.Contains("data-tile=\"Drafts\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/Applications?status=Draft\"", html, StringComparison.Ordinal);
        Assert.Contains("data-section=\"attention\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("on its way", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PCB LOS", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Credit Operations", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RoleCodes.Underwriter, "Underwriting")]
    [InlineData(RoleCodes.SeniorUnderwriter, "Underwriting")]
    [InlineData(RoleCodes.CreditCommittee, "Committee")]
    [InlineData(RoleCodes.Compliance, "Compliance")]
    [InlineData(RoleCodes.Auditor, "Audit")]
    public async Task Each_role_lands_on_its_own_dashboard(string roleCode, string dashboard)
    {
        var (userName, _, password) = await CreateUserAsync(roleCode);
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var html = await browser.GetPageAsync("/");

        Assert.Contains($"data-dashboard=\"{dashboard}\"", html, StringComparison.Ordinal);
    }

    private static async Task PostActionAsync(
        WebSession browser, UserId id, string action, IDictionary<string, string>? fields = null)
    {
        var response = await browser.PostFormAsync($"/Admin/Users/Details/{id}", $"/Admin/Users/{action}/{id}", fields ?? new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
