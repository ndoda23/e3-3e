using System.Net;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Admin › Authority limits: both limit types, their rules, and the immediate effect on deciders.</summary>
public sealed class AuthorityLimitAdministrationTests(SqlServerContainer sql) : WebTestBase(sql)
{
    [Fact]
    public async Task Overview_shows_the_seeded_limits_of_both_types()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;

        var html = WebUtility.HtmlDecode(await browser.GetPageAsync("/Admin/AuthorityLimits"));

        Assert.Contains("data-limit=\"UW-Senior-GEL\"", html, StringComparison.Ordinal);
        Assert.Contains("₾50,000", html, StringComparison.Ordinal);
        Assert.Contains("data-limit=\"BM--GEL\"", html, StringComparison.Ordinal);
        Assert.Contains("₾150,000", html, StringComparison.Ordinal);
        Assert.Contains("Below 2 pp", html, StringComparison.Ordinal);
        Assert.Contains("At least 15%", html, StringComparison.Ordinal);
        Assert.Contains("data-limit=\"DIR-Pricing\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Raising_a_limit_shows_on_the_deciders_dashboard_at_once_and_is_audited()
    {
        var (userName, _, password) = await CreateSeniorUnderwriterAsync();
        using var underwriter = NewSession();
        await underwriter.LoginAsync(userName, password);
        var (admin, _) = await SignInAsAdministratorAsync();
        using var _admin = admin;
        var limitId = await LimitIdAsync(RoleCodes.Underwriter, AuthorityLevel.Senior);

        var response = await admin.PostFormAsync(
            $"/Admin/AuthorityLimits/EditCredit/{limitId}", $"/Admin/AuthorityLimits/EditCredit/{limitId}",
            new Dictionary<string, string> { ["MaxAmount"] = "60000" });

        Assert.Equal("/Admin/AuthorityLimits", response.Headers.Location!.OriginalString);
        Assert.Contains("₾60,000", WebUtility.HtmlDecode(await underwriter.GetPageAsync("/")), StringComparison.Ordinal);
        Assert.True(await QueryAsync(db => db.AuditLogs.AnyAsync(
            l => l.EntityType == "AuthorityLimit" && l.EntityId == limitId.ToString() && l.Action == "Updated", Ct)));
    }

    [Fact]
    public async Task A_limit_in_another_currency_is_added_to_the_dashboard()
    {
        var (userName, _, password) = await CreateSeniorUnderwriterAsync();
        using var underwriter = NewSession();
        await underwriter.LoginAsync(userName, password);
        var (admin, _) = await SignInAsAdministratorAsync();
        using var _admin = admin;

        var response = await admin.PostFormAsync("/Admin/AuthorityLimits/CreateCredit", "/Admin/AuthorityLimits/CreateCredit", new Dictionary<string, string>
        {
            ["RoleId"] = (await RoleIdAsync(RoleCodes.Underwriter)).ToString(),
            ["AuthorityLevel"] = "Senior",
            ["Currency"] = "USD",
            ["MaxAmount"] = "20000",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("₾50,000 · $20,000", WebUtility.HtmlDecode(await underwriter.GetPageAsync("/")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RoleCodes.Underwriter, "", "GEL", "This role has authority levels")]
    [InlineData(RoleCodes.BranchManager, "Senior", "GEL", "This role has no authority levels")]
    [InlineData(RoleCodes.Underwriter, "Senior", "GEL", "A limit already exists for this role, level and currency.")]
    [InlineData(RoleCodes.SystemAdministrator, "", "GEL", "Authority limits apply only to business roles.")]
    public async Task Credit_limit_rules_are_enforced(string roleCode, string level, string currency, string expected)
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var before = await QueryAsync(db => db.AuthorityLimits.CountAsync(Ct));

        var response = await browser.PostFormAsync("/Admin/AuthorityLimits/CreateCredit", "/Admin/AuthorityLimits/CreateCredit", new Dictionary<string, string>
        {
            ["RoleId"] = (await RoleIdAsync(roleCode)).ToString(),
            ["AuthorityLevel"] = level,
            ["Currency"] = currency,
            ["MaxAmount"] = "1000",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.Equal(before, await QueryAsync(db => db.AuthorityLimits.CountAsync(Ct)));
    }

    [Fact]
    public async Task Deleting_the_only_limit_of_a_level_means_its_decisions_are_escalated()
    {
        var (userName, _, password) = await CreateSeniorUnderwriterAsync();
        using var underwriter = NewSession();
        await underwriter.LoginAsync(userName, password);
        var (admin, _) = await SignInAsAdministratorAsync();
        using var _admin = admin;
        var limitId = await LimitIdAsync(RoleCodes.Underwriter, AuthorityLevel.Senior);

        var modal = await (await admin.HtmxGetAsync($"/Admin/AuthorityLimits/ConfirmDelete/{limitId}")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("role=\"dialog\"", modal, StringComparison.Ordinal);
        var response = await admin.PostWithTokenAsync($"/Admin/AuthorityLimits/Delete/{limitId}", new Dictionary<string, string>(), WebSession.Token(modal));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(await QueryAsync(db => db.AuthorityLimits.AnyAsync(l => l.Id == new AuthorityLimitId(limitId), Ct)));
        Assert.Contains("none set", await underwriter.GetPageAsync("/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_a_pricing_threshold_means_no_limit_on_it()
    {
        var (browser, _) = await SignInAsAdministratorAsync();
        using var _browser = browser;
        var roleId = new RoleId(await RoleIdAsync(RoleCodes.Head));
        var limitId = await QueryAsync(db => db.AuthorityLimits
            .Where(l => l.RoleId == roleId && l.LimitType == LimitType.PricingException).Select(l => l.Id).SingleAsync(Ct));

        var response = await browser.PostFormAsync(
            $"/Admin/AuthorityLimits/EditPricing/{limitId.Value}", $"/Admin/AuthorityLimits/EditPricing/{limitId.Value}",
            new Dictionary<string, string> { ["MaxRateGapPp"] = "", ["MinCustomerRoaePct"] = "12.5" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var limit = await QueryAsync(db => db.AuthorityLimits.SingleAsync(l => l.Id == limitId, Ct));
        Assert.Null(limit.MaxRateGapPp);
        Assert.Equal(12.5m, limit.MinCustomerRoaePct);
    }

    [Fact]
    public async Task Underwriters_cannot_open_authority_limits()
    {
        var (userName, _, password) = await CreateSeniorUnderwriterAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var response = await browser.GetAsync("/Admin/AuthorityLimits");

        Assert.Equal("/Account/AccessDenied", response.Headers.Location!.AbsolutePath);
    }

    private Task<Guid> RoleIdAsync(string code) =>
        QueryAsync(db => db.Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync(Ct));

    private async Task<Guid> LimitIdAsync(string roleCode, AuthorityLevel level)
    {
        var roleId = new RoleId(await RoleIdAsync(roleCode));
        var id = await QueryAsync(db => db.AuthorityLimits
            .Where(l => l.RoleId == roleId && l.AuthorityLevel == level && l.LimitType == LimitType.CreditDecision)
            .Select(l => l.Id)
            .SingleAsync(Ct));
        return id.Value;
    }
}
