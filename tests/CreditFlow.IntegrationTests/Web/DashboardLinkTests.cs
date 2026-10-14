using System.Net;
using System.Text.RegularExpressions;
using CreditFlow.Application.Dashboard;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Roles;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.Infrastructure.Outbox;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>
/// Every dashboard tile opens a list whose total is the tile's number, for the same user, over the Development demo
/// data (applications in every status in both branches, after the live pre-check).
/// </summary>
public sealed partial class DashboardLinkTests(SqlServerContainer sql) : WebTestBase(sql)
{
    protected override string Environment => "Development";

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["Seed:DevUsers:Enabled"] = "true",
        ["Seed:DevUsers:Password"] = InfrastructureHost.DevPassword,
        ["Seed:DemoData:Enabled"] = "true",
        ["Mocks:BureauTimeout"] = "00:00:00.200",
        ["Dashboard:LargeDisbursementGel"] = "20000", // so that the demo payouts include large ones
    };

    public static TheoryData<string, string?, DashboardMetric> Tiles => new()
    {
        { RoleCodes.Operator, BranchSeedCatalog.Vake, DashboardMetric.Drafts },
        { RoleCodes.Operator, BranchSeedCatalog.Vake, DashboardMetric.DocumentsPending },
        { RoleCodes.Operator, BranchSeedCatalog.Vake, DashboardMetric.AwaitingSignature },
        { RoleCodes.Operator, BranchSeedCatalog.Vake, DashboardMetric.AwaitingDisbursement },
        { RoleCodes.Operator, BranchSeedCatalog.Vake, DashboardMetric.PayoutsPending },
        { RoleCodes.Operator, BranchSeedCatalog.Vake, DashboardMetric.PayoutsFailed },
        { RoleCodes.Operator, BranchSeedCatalog.Batumi, DashboardMetric.StuckInPreCheck },
        { RoleCodes.Underwriter, null, DashboardMetric.Inbox },
        { RoleCodes.SeniorUnderwriter, null, DashboardMetric.Inbox },
        { RoleCodes.SeniorUnderwriter, null, DashboardMetric.Escalated },
        { RoleCodes.SeniorUnderwriter, null, DashboardMetric.Overridable },
        { RoleCodes.CreditCommittee, null, DashboardMetric.AwaitingVote },
        { RoleCodes.Compliance, null, DashboardMetric.ComplianceReviews },
        { RoleCodes.Auditor, null, DashboardMetric.RecentOverrides },
        { RoleCodes.Auditor, null, DashboardMetric.RecentRejections },
        { RoleCodes.Auditor, null, DashboardMetric.ComplianceClearances },
        { RoleCodes.Auditor, null, DashboardMetric.LargeDisbursements },
        { RoleCodes.Auditor, null, DashboardMetric.PayoutIncidents },
        { RoleCodes.Auditor, null, DashboardMetric.RecentEscalations },
        { RoleCodes.Auditor, null, DashboardMetric.RecentAccessChanges },
        { RoleCodes.Admin, null, DashboardMetric.LockedUsers },
        { RoleCodes.Admin, null, DashboardMetric.UsersWithoutMfa },
        { RoleCodes.Admin, null, DashboardMetric.RecentAccessChanges },
    };

    [GeneratedRegex("""<a class="kpi[^"]*" href="(?<href>[^"]+)" role="listitem" data-tile="(?<tile>\w+)" data-count="(?<count>\d+)">""")]
    private static partial Regex TilePattern();

    [GeneratedRegex("""data-total="(?<total>\d+)""")]
    private static partial Regex TotalPattern();

    [Theory]
    [MemberData(nameof(Tiles))]
    public async Task The_tile_count_equals_the_total_of_the_list_it_links_to(string role, string? branch, DashboardMetric metric)
    {
        await Factory.Services.GetRequiredService<OutboxProcessor>().ProcessAllDueAsync(Ct); // the live pre-check
        if (role == RoleCodes.Admin)
        {
            await LockDemoUserAsync(); // so that the locked-users tile is not trivially zero
        }

        var (userName, _, password) = await CreateUserAsync(role, branch);
        using var browser = NewSession();
        Assert.Equal(HttpStatusCode.Redirect, (await browser.LoginAsync(userName, password)).StatusCode);

        var tile = TilePattern().Matches(await browser.GetPageAsync("/")).SingleOrDefault(m => m.Groups["tile"].Value == metric.ToString());
        Assert.True(tile is not null, $"{role}: no linked tile {metric} on the dashboard.");
        var href = WebUtility.HtmlDecode(tile.Groups["href"].Value);
        var list = await browser.GetPageAsync(href);
        var total = TotalPattern().Match(list);

        Assert.True(total.Success, $"{href} shows no total.");
        Assert.Equal(int.Parse(tile.Groups["count"].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(total.Groups["total"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_auditor_sees_every_application_regardless_of_status_and_branch_read_only()
    {
        await Factory.Services.GetRequiredService<OutboxProcessor>().ProcessAllDueAsync(Ct);
        var (userName, _, password) = await CreateUserAsync(RoleCodes.Auditor);
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);
        var all = await QueryAsync(db => db.LoanApplications.CountAsync(Ct));
        var oneOfEach = await QueryAsync(db => db.LoanApplications
            .GroupBy(a => new { a.Status, a.BranchId }).Select(g => g.Min(a => a.Id)).ToListAsync(Ct));

        var list = await browser.GetPageAsync("/Applications");

        Assert.Equal(all.ToString(System.Globalization.CultureInfo.InvariantCulture), TotalPattern().Match(list).Groups["total"].Value);
        Assert.True(oneOfEach.Count > 20, "The demo data covers every status in both branches.");
        foreach (var page in oneOfEach.Select(id => $"/Applications/Details/{id.Value}").Concat(["/Applications", "/Loans", "/Reports", "/Admin/AuditLog", "/Clients"]))
        {
            var html = await browser.GetPageAsync(page);
            Assert.Equal(["/Account/Logout"], WebSession.PostForms(html).Select(f => f.Action)); // read-only: no action forms
            Assert.DoesNotContain("hx-post", html, StringComparison.Ordinal);
        }

        var loan = await QueryAsync(db => db.Loans.OrderBy(l => l.Id).Select(l => l.Id.Value).FirstAsync(Ct));
        Assert.Equal(["/Account/Logout"], WebSession.PostForms(await browser.GetPageAsync($"/Loans/Details/{loan}")).Select(f => f.Action));
    }

    private async Task LockDemoUserAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await users.SetLockoutEndDateAsync((await users.FindByNameAsync("davit.maisuradze"))!, Factory.Clock.GetUtcNow().AddDays(1));
    }
}
