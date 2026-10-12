using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Users;
using CreditFlow.IntegrationTests.Infrastructure;
using CreditFlow.Web.Areas.Admin.Models;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Admin › Audit log: filters, details with before / after, read-only.</summary>
public sealed class AuditLogTests(SqlServerContainer sql) : WebTestBase(sql)
{
    [Fact]
    public async Task Action_filter_returns_only_matching_rows_with_htmx()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        Assert.Contains("data-audit=\"", await browser.GetPageAsync("/Admin/AuditLog"), StringComparison.Ordinal);

        var rows = await browser.HtmxGetAsync($"/Admin/AuditLog/Rows?event={AuditActions.LoginSucceeded}");
        var html = await rows.Content.ReadAsStringAsync(Ct);

        var actions = Regex.Matches(html, "data-audit=\"(?<a>[^\"]+)\"").Select(m => m.Groups["a"].Value).ToList();
        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.Equal(AuditActions.LoginSucceeded, a));
        Assert.Equal($"/Admin/AuditLog?event={AuditActions.LoginSucceeded}", rows.Headers.GetValues("HX-Push-Url").Single());
    }

    [Fact]
    public async Task Date_filter_uses_whole_local_days()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var today = DateOnly.FromDateTime(Factory.Clock.GetUtcNow().ToLocalTime().DateTime);
        string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        async Task<int> CountAsync(string query) =>
            Regex.Matches(await (await browser.HtmxGetAsync($"/Admin/AuditLog/Rows?{query}")).Content.ReadAsStringAsync(Ct), "data-audit=\"").Count;

        Assert.True(await CountAsync($"from={Day(today)}&to={Day(today)}") > 0);
        Assert.Equal(0, await CountAsync($"from={Day(today.AddDays(1))}"));
        Assert.Equal(0, await CountAsync($"to={Day(today.AddDays(-1))}"));
    }

    [Fact]
    public async Task A_user_change_shows_the_changed_fields_before_and_after()
    {
        var (userName, id, _) = await CreateOperatorAsync();
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var (operatorRole, vake) = await QueryAsync(async db => (
            await db.Roles.Where(r => r.Code == RoleCodes.Operator).Select(r => r.Id).SingleAsync(Ct),
            (await db.Branches.Where(b => b.Code == BranchSeedCatalog.Vake).Select(b => b.Id).SingleAsync(Ct)).Value));
        await browser.PostFormAsync($"/Admin/Users/Edit/{id}", $"/Admin/Users/Edit/{id}", new Dictionary<string, string>
        {
            ["FullName"] = "Renamed Operator",
            ["UserName"] = userName,
            ["Email"] = $"{userName}@pcb.ge",
            ["RoleId"] = operatorRole.ToString(),
            ["BranchId"] = vake.ToString(),
            ["Capacity"] = "10",
        });
        var recordId = await QueryAsync(db => db.AuditLogs
            .Where(l => l.EntityType == "User" && l.EntityId == id.ToString() && l.Action == AuditActions.Updated)
            .Select(l => l.Id).SingleAsync(Ct));

        var html = await browser.GetPageAsync($"/Admin/AuditLog/Details/{recordId.Value}");

        Assert.Matches("data-property=\"FullName\"[\\s\\S]*?Test OP[\\s\\S]*?Renamed Operator", html);
        Assert.Contains("class=\"changed\" data-property=\"FullName\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_permission_change_shows_removed_and_added_permissions()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var roleId = await QueryAsync(db => db.Roles.Where(r => r.Code == RoleCodes.Underwriter).Select(r => r.Id).SingleAsync(Ct));
        var granted = await QueryAsync(db => db.RolePermissions.Where(p => p.RoleId == roleId).Select(p => p.Permission).ToListAsync(Ct));
        var page = await browser.GetPageAsync($"/Admin/Roles/Permissions/{roleId}");
        await browser.PostPairsWithTokenAsync(
            $"/Admin/Roles/Permissions/{roleId}",
            granted.Where(p => p != Permissions.Reports.View).Append(Permissions.Reports.Export).Select(p => KeyValuePair.Create("permissions", p)),
            WebSession.Token(page));
        var recordId = await QueryAsync(db => db.AuditLogs
            .Where(l => l.Action == AuditActions.RolePermissionsChanged).Select(l => l.Id).SingleAsync(Ct));

        var html = await browser.GetPageAsync($"/Admin/AuditLog/Details/{recordId.Value}");

        Assert.Contains($"data-removed=\"{Permissions.Reports.View}\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-added=\"{Permissions.Reports.Export}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Actor_filter_shows_only_that_users_actions()
    {
        var (browser, adminId) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var adminName = await QueryAsync(db => db.Users.Where(u => u.Id == adminId.Value).Select(u => u.UserName).SingleAsync(Ct));

        var html = await browser.GetPageAsync($"/Admin/AuditLog?user={adminId}");

        Assert.Contains("Test SYSADMIN", html, StringComparison.Ordinal);
        var names = Regex.Matches(html, "<td>(?<n>[^<]+)</td>\\s*<td><span>").Select(m => m.Groups["n"].Value.Trim()).Distinct().ToList();
        Assert.All(names, n => Assert.Equal(adminName, n));
    }

    [Fact]
    public async Task The_audit_log_cannot_be_changed_through_the_web()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var page = await browser.GetPageAsync("/Admin/AuditLog");
        var before = await QueryAsync(db => db.AuditLogs.CountAsync(Ct));
        var any = await QueryAsync(db => db.AuditLogs.Select(l => l.Id).FirstAsync(Ct));

        foreach (var path in new[] { $"/Admin/AuditLog/Delete/{any.Value}", $"/Admin/AuditLog/Edit/{any.Value}", $"/Admin/AuditLog/Details/{any.Value}" })
        {
            var response = await browser.PostWithTokenAsync(path, new Dictionary<string, string>(), WebSession.Token(page));
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{path}: {response.StatusCode}");
        }

        Assert.Equal(before, await QueryAsync(db => db.AuditLogs.CountAsync(Ct)));
    }

    [Fact]
    public async Task Business_users_without_audit_permission_cannot_open_the_audit_log()
    {
        var (userName, _, password) = await CreateOperatorAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var response = await browser.GetAsync("/Admin/AuditLog");

        Assert.Equal("/Account/AccessDenied", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Auditor_reads_the_audit_log_without_any_administration_access()
    {
        var (_, operatorId, _) = await CreateOperatorAsync(); // writes a "User Created" record
        var (userName, _, password) = await CreateUserAsync(RoleCodes.Auditor);
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var dashboard = await browser.GetPageAsync("/");
        var list = await browser.GetPageAsync("/Admin/AuditLog");

        Assert.Contains("data-nav=\"Audit\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("data-nav=\"Users\"", dashboard, StringComparison.Ordinal);
        Assert.Contains(operatorId.ToString()[..8], list, StringComparison.Ordinal);
        Assert.DoesNotContain($"/Admin/Users/Details/{operatorId}", list, StringComparison.Ordinal);
        Assert.Equal("/Account/AccessDenied", (await browser.GetAsync("/Admin/Users")).Headers.Location!.AbsolutePath);
    }

    // ----- Before / after preparation (no database) -----

    [Fact]
    public void Entity_changes_become_a_property_table()
    {
        var changes = AuditChanges.Build("""{"Name":"Vake","City":"Tbilisi"}""", """{"Name":"Vake Park","City":"Tbilisi"}""");

        Assert.True(changes.IsPropertyTable);
        Assert.Equal(["Name", "City"], changes.Properties.Select(p => p.Name));
        Assert.True(changes.Properties[0].IsChanged);
        Assert.False(changes.Properties[1].IsChanged);
    }

    [Fact]
    public void A_created_entity_has_only_after_values()
    {
        var changes = AuditChanges.Build(null, """{"Code":"GLD","IsActive":true}""");

        Assert.All(changes.Properties, p => Assert.Null(p.Before));
        Assert.Equal("true", changes.Properties.Single(p => p.Name == "IsActive").After);
    }

    [Fact]
    public void Lists_become_removed_and_added_items()
    {
        var changes = AuditChanges.Build("""["a","b"]""", """["b","c"]""");

        Assert.Equal(["a"], changes.Removed);
        Assert.Equal(["c"], changes.Added);
    }

    [Fact]
    public void Other_values_are_shown_as_text()
    {
        var changes = AuditChanges.Build(null, "\"InvalidPassword\"");

        Assert.False(changes.IsPropertyTable);
        Assert.Equal("InvalidPassword", changes.RawAfter);
        Assert.True(AuditChanges.Build(null, null).IsEmpty);
    }
}
