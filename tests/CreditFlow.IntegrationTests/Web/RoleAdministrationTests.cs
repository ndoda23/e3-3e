using System.Net;
using System.Text.RegularExpressions;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Auditing;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Admin › Roles &amp; permissions: the matrix, the per-role editor and segregation of duties.</summary>
public sealed class RoleAdministrationTests(SqlServerContainer sql) : WebTestBase(sql)
{
    [Fact]
    public async Task Matrix_shows_every_role_and_every_seeded_grant()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var grants = await QueryAsync(db => db.RolePermissions.CountAsync(Ct));

        var html = await browser.GetPageAsync("/Admin/Roles");

        Assert.Equal(10, Regex.Matches(html, "<tr data-role=\"").Count);
        Assert.Equal(grants, Regex.Matches(html, "class=\"cell granted\"").Count);
    }

    [Fact]
    public async Task Editor_mirrors_segregation_of_duties_and_locked_administrator_permissions()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;

        var underwriter = await browser.GetPageAsync($"/Admin/Roles/Permissions/{await RoleIdAsync(RoleCodes.Underwriter)}");
        var administrator = await browser.GetPageAsync($"/Admin/Roles/Permissions/{await RoleIdAsync(RoleCodes.SystemAdministrator)}");

        Assert.Matches("value=\"admin.users.view\"[^>]*disabled", underwriter);
        Assert.Matches(@"value=""applications.decide""\s+checked=""checked""", underwriter);
        Assert.Matches("value=\"applications.view-full\"[^>]*disabled", administrator);
        Assert.Contains("<input type=\"hidden\" name=\"permissions\" value=\"admin.users.manage\" />", administrator, StringComparison.Ordinal);
        Assert.Contains("<input type=\"hidden\" name=\"permissions\" value=\"admin.roles.manage\" />", administrator, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_a_permission_applies_on_the_next_request_without_signing_out()
    {
        var (userName, _, password) = await CreateSeniorUnderwriterAsync();
        using var underwriter = NewSession();
        await underwriter.LoginAsync(userName, password);
        Assert.Contains("data-nav=\"Reports\"", await underwriter.GetPageAsync("/"), StringComparison.Ordinal);

        var (admin, _) = await SignInAsAdministratorAsync();
        using var _admin = admin;
        var roleId = await RoleIdAsync(RoleCodes.Underwriter);
        var granted = await QueryAsync(db => db.RolePermissions
            .Where(p => p.RoleId == roleId).Select(p => p.Permission).ToListAsync(Ct));
        var page = await admin.GetPageAsync($"/Admin/Roles/Permissions/{roleId}");

        var response = await admin.PostPairsWithTokenAsync(
            $"/Admin/Roles/Permissions/{roleId}",
            granted.Where(p => p != Permissions.Reports.View).Select(p => KeyValuePair.Create("permissions", p)),
            WebSession.Token(page));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var dashboard = await underwriter.GetPageAsync("/");
        Assert.DoesNotContain("data-nav=\"Reports\"", dashboard, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, (await underwriter.GetAsync("/Reports")).StatusCode);
        Assert.True(await QueryAsync(db => db.AuditLogs.AnyAsync(
            l => l.Action == AuditActions.RolePermissionsChanged && l.EntityId == RoleCodes.Underwriter, Ct)));
    }

    [Fact]
    public async Task Administration_permissions_cannot_be_given_to_a_business_role_and_the_selection_is_kept()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var roleId = await RoleIdAsync(RoleCodes.Underwriter);
        var page = await browser.GetPageAsync($"/Admin/Roles/Permissions/{roleId}");

        var response = await browser.PostPairsWithTokenAsync(
            $"/Admin/Roles/Permissions/{roleId}",
            [KeyValuePair.Create("permissions", Permissions.Reports.View), KeyValuePair.Create("permissions", Permissions.Admin.UsersView)],
            WebSession.Token(page));
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"value=""applications.decide""\s+checked", html);
        Assert.False(await QueryAsync(db => db.RolePermissions.AnyAsync(
            p => p.RoleId == roleId && p.Permission == Permissions.Admin.UsersView, Ct)));
        Assert.True(await QueryAsync(db => db.RolePermissions.AnyAsync(
            p => p.RoleId == roleId && p.Permission == Permissions.Applications.Decide, Ct)));
    }

    [Fact]
    public async Task Administrator_role_must_keep_user_and_role_management()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var roleId = await RoleIdAsync(RoleCodes.SystemAdministrator);
        var page = await browser.GetPageAsync($"/Admin/Roles/Permissions/{roleId}");

        var response = await browser.PostPairsWithTokenAsync(
            $"/Admin/Roles/Permissions/{roleId}",
            [KeyValuePair.Create("permissions", Permissions.Admin.RolesManage)],
            WebSession.Token(page));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("must keep the permission", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.True(await QueryAsync(db => db.RolePermissions.AnyAsync(
            p => p.RoleId == roleId && p.Permission == Permissions.Admin.UsersManage, Ct)));
    }

    [Fact]
    public async Task Requiring_mfa_for_a_role_sends_its_users_to_authenticator_setup()
    {
        var (userName, _, password) = await CreateSeniorUnderwriterAsync();
        using var underwriter = NewSession();
        await underwriter.LoginAsync(userName, password);

        var (admin, _) = await SignInAsAdministratorAsync();
        using var _admin = admin;
        var roleId = await RoleIdAsync(RoleCodes.Underwriter);
        var page = await admin.GetPageAsync($"/Admin/Roles/Permissions/{roleId}");
        await admin.PostWithTokenAsync(
            $"/Admin/Roles/RequireMfa/{roleId}", new Dictionary<string, string> { ["require"] = "true" }, WebSession.Token(page));

        Assert.True(await QueryAsync(db => db.Roles.Where(r => r.Id == roleId).Select(r => r.RequireMfa).SingleAsync(Ct)));
        Assert.Equal("/Account/EnableAuthenticator", (await underwriter.GetAsync("/")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Underwriters_cannot_open_the_role_editor()
    {
        var (userName, _, password) = await CreateSeniorUnderwriterAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var response = await browser.GetAsync("/Admin/Roles");

        Assert.Equal("/Account/AccessDenied", response.Headers.Location!.AbsolutePath);
    }

    private Task<Guid> RoleIdAsync(string code) =>
        QueryAsync(db => db.Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync(Ct));
}
