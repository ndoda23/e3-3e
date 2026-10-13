using System.Net;
using System.Text.RegularExpressions;
using CreditFlow.Application.Dashboard;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Roles;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.Infrastructure.Outbox;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
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
        { RoleCodes.Auditor, null, DashboardMetric.RecentDisbursements },
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

    private async Task LockDemoUserAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await users.SetLockoutEndDateAsync((await users.FindByNameAsync("davit.maisuradze"))!, Factory.Clock.GetUtcNow().AddDays(1));
    }
}
