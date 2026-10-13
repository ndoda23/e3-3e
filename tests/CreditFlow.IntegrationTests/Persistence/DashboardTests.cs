using CreditFlow.Application.Dashboard;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CreditFlow.IntegrationTests.Persistence;

/// <summary>
/// The dashboards count in SQL within the viewer's data scope: a branch operator never counts another branch, the
/// decision inboxes follow the inbox rules, the Auditor sees the whole bank, the Administrator sees accounts.
/// </summary>
public sealed class DashboardTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    private const string VakeOperator = "anna.gelashvili";
    private const string BatumiOperator = "davit.maisuradze";
    private const string Underwriter = "nika.beridze";
    private const string SeniorUnderwriter = "tamta.tatishvili";
    private const string CommitteeMember = "irakli.tsereteli";
    private const string ComplianceOfficer = "nino.kvaratskhelia";
    private const string Auditor = "levan.abashidze";

    private readonly FakeTimeProvider _clock = new(BankTime.ToInstant(new DateOnly(2026, 10, 12), new TimeOnly(10, 0)));

    protected override string Environment => "Development";

    protected override bool SeedDevUsers => true;

    protected override TimeProvider? Clock => _clock;

    [Fact]
    public async Task An_operator_counts_and_lists_only_their_branch()
    {
        var vake = await UserAsync(VakeOperator);
        var batumi = await UserAsync(BatumiOperator);
        var senior = await UserAsync(SeniorUnderwriter);
        var lending = new TestLending(Host.Services, _clock);
        var vakeApproved = await lending.ApprovedAsync(LendingSeedCatalog.Consumer, 10_000m, vake, [senior]);
        var vakeStuck = await lending.InReviewAsync(LendingSeedCatalog.Consumer, 5_000m, vake);
        await SetStatusAsync(vakeStuck, ApplicationStatus.PreCheck, _clock.GetUtcNow().AddHours(-2));
        lending.BranchCode = BranchSeedCatalog.Batumi;
        var batumiApproved = await lending.ApprovedAsync(LendingSeedCatalog.Consumer, 10_000m, batumi, [senior]);

        var vakeDashboard = await DashboardAsync(vake);
        var batumiDashboard = await DashboardAsync(batumi);

        Assert.Equal(1, Count(vakeDashboard, DashboardMetric.AwaitingSignature));
        Assert.Equal(1, Count(vakeDashboard, DashboardMetric.StuckInPreCheck));
        Assert.True(vakeDashboard.Tiles.Single(t => t.Metric == DashboardMetric.StuckInPreCheck).Alert);
        Assert.Equal(vakeStuck.Value.ToString(), vakeDashboard.Attention[0].Id); // stuck first, then the oldest waiting
        Assert.Equal(DashboardMetric.StuckInPreCheck, vakeDashboard.Attention[0].Reason);
        Assert.Contains(vakeDashboard.Attention, i => i.Id == vakeApproved.Value.ToString());
        Assert.DoesNotContain(vakeDashboard.Attention, i => i.Id == batumiApproved.Value.ToString());

        Assert.Equal(1, Count(batumiDashboard, DashboardMetric.AwaitingSignature));
        Assert.Equal(0, Count(batumiDashboard, DashboardMetric.StuckInPreCheck));
        Assert.Equal([batumiApproved.Value.ToString()], batumiDashboard.Attention.Select(i => i.Id));
    }

    [Fact]
    public async Task Decision_dashboards_follow_the_inbox_rules_and_seniors_see_overridable_files()
    {
        var vake = await UserAsync(VakeOperator);
        var lending = new TestLending(Host.Services, _clock);
        var small = await lending.InReviewAsync(LendingSeedCatalog.Consumer, 10_000m, vake);
        var large = await lending.InReviewAsync(LendingSeedCatalog.Mortgage, 150_000m, vake);
        _clock.Advance(TimeSpan.FromHours(5));
        await FailLatestScoringAsync(small);

        var underwriter = await DashboardAsync(await UserAsync(Underwriter));
        var senior = await DashboardAsync(await UserAsync(SeniorUnderwriter));
        var committee = await DashboardAsync(await UserAsync(CommitteeMember));
        var compliance = await DashboardAsync(await UserAsync(ComplianceOfficer));

        Assert.Equal(1, Count(underwriter, DashboardMetric.Inbox));
        Assert.Equal(small.Value.ToString(), Assert.Single(underwriter.Attention).Id);
        Assert.Equal(_clock.GetUtcNow().AddHours(-5), underwriter.OldestWaitingSince);
        Assert.DoesNotContain(underwriter.Tiles, t => t.Metric == DashboardMetric.Overridable);
        Assert.Equal(1, Count(senior, DashboardMetric.Overridable));
        Assert.Equal(0, Count(senior, DashboardMetric.Escalated));

        Assert.Equal(1, Count(committee, DashboardMetric.AwaitingVote));
        Assert.Equal(large.Value.ToString(), Assert.Single(committee.Attention).Id);
        Assert.Equal(0, Count(compliance, DashboardMetric.ComplianceReviews));
        Assert.Empty(compliance.Attention);
    }

    [Fact]
    public async Task The_auditor_sees_the_whole_bank_read_only()
    {
        var vake = await UserAsync(VakeOperator);
        var lending = new TestLending(Host.Services, _clock);
        var vakeRejected = await lending.InReviewAsync(LendingSeedCatalog.Consumer, 5_000m, vake);
        lending.BranchCode = BranchSeedCatalog.Batumi;
        var batumiRejected = await lending.InReviewAsync(LendingSeedCatalog.Consumer, 5_000m, await UserAsync(BatumiOperator));
        await SetStatusAsync(vakeRejected, ApplicationStatus.Rejected, _clock.GetUtcNow());
        await SetStatusAsync(batumiRejected, ApplicationStatus.Rejected, _clock.GetUtcNow().AddDays(-40)); // outside the window

        var audit = await DashboardAsync(await UserAsync(Auditor));
        var vakeOperator = await DashboardAsync(vake);

        Assert.Equal([DashboardMetric.RecentOverrides, DashboardMetric.RecentRejections, DashboardMetric.RecentDisbursements], audit.Tiles.Select(t => t.Metric));
        Assert.Equal(1, Count(audit, DashboardMetric.RecentRejections));
        Assert.DoesNotContain(vakeOperator.Tiles, t => t.Metric == DashboardMetric.RecentRejections);
    }

    [Fact]
    public async Task The_administrator_sees_locked_accounts_and_accounts_without_mfa()
    {
        var locked = await UserAsync(BatumiOperator);
        await Host.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            await users.SetLockoutEndDateAsync((await users.FindByIdAsync(locked.ToString()))!, _clock.GetUtcNow().AddDays(1));
        });
        var admin = await UserAsync(InfrastructureHost.AdminUserName);
        var withoutMfa = await Host.QueryAsync(db => db.UserDirectory.CountAsync(u => u.IsActive && !u.TwoFactorEnabled, Ct));

        var dashboard = await DashboardAsync(admin);
        var operatorDashboard = await DashboardAsync(await UserAsync(VakeOperator));

        Assert.Equal(1, Count(dashboard, DashboardMetric.LockedUsers));
        Assert.Equal(withoutMfa, Count(dashboard, DashboardMetric.UsersWithoutMfa));
        Assert.Contains(dashboard.Attention, i => i.Kind == AttentionKind.User && i.Id == locked.ToString());
        Assert.DoesNotContain(operatorDashboard.Tiles, t => t.Metric == DashboardMetric.LockedUsers);
    }

    private static int Count(DashboardData data, DashboardMetric metric) => data.Tiles.Single(t => t.Metric == metric).Count;

    private async Task<DashboardData> DashboardAsync(UserId user) =>
        (await Host.InScopeAsAsync(user, sp => sp.GetRequiredService<DashboardService>().GetAsync(Ct)))!;

    private Task<int> SetStatusAsync(LoanApplicationId id, ApplicationStatus status, DateTimeOffset at) =>
        Host.QueryAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE LoanApplications SET Status = {status.ToString()}, StatusChangedAt = {at} WHERE Id = {id.Value}", Ct));

    /// <summary>The latest scoring run fails an overridable rule (approval then needs a senior's override).</summary>
    private Task<int> FailLatestScoringAsync(LoanApplicationId id)
    {
        const string runs = """
            [{"ruleSetVersion":1,"scoredAt":"2026-10-12T06:00:00+00:00","results":[{"ruleCode":"PTI","passed":true,"overridable":true,"measured":"30 %","threshold":"≤ 45 %","reason":"ok"}]},
             {"ruleSetVersion":1,"scoredAt":"2026-10-12T06:30:00+00:00","results":[{"ruleCode":"PTI","passed":false,"overridable":true,"measured":"52 %","threshold":"≤ 45 %","reason":"too high"}]}]
            """;
        return Host.QueryAsync(db => db.Database.ExecuteSqlAsync($"UPDATE LoanApplications SET ScoringRunsJson = {runs} WHERE Id = {id.Value}", Ct));
    }

    private Task<UserId> UserAsync(string userName) =>
        Host.QueryAsync(async db => new UserId(await db.UserDirectory.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(Ct)));
}
