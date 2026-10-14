using CreditFlow.Application.Applications;
using CreditFlow.Application.Dashboard;
using CreditFlow.Application.Disbursements;
using CreditFlow.Application.Users;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.ExternalServices;
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

    /// <summary>Low enough for a 10,000 GEL test loan to count as a large payout.</summary>
    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["Dashboard:LargeDisbursementGel"] = "10000",
    };

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
        Assert.Equal([DashboardMetric.Inbox], underwriter.Tiles.Select(t => t.Metric)); // escalated files never reach a plain UW
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

        Assert.Equal(
            [DashboardMetric.RecentOverrides, DashboardMetric.RecentRejections, DashboardMetric.ComplianceClearances, DashboardMetric.LargeDisbursements,
             DashboardMetric.PayoutIncidents, DashboardMetric.RecentEscalations, DashboardMetric.RecentAccessChanges],
            audit.Tiles.Select(t => t.Metric));
        Assert.Equal(1, Count(audit, DashboardMetric.RecentRejections));
        Assert.DoesNotContain(vakeOperator.Tiles, t => t.Metric == DashboardMetric.RecentRejections);
    }

    [Fact]
    public async Task The_auditor_feed_shows_every_kind_of_exception_newest_first()
    {
        var vake = await UserAsync(VakeOperator);
        var senior = await UserAsync(SeniorUnderwriter);
        var lending = new TestLending(Host.Services, _clock);

        // Oldest first: a large payout, a failed payout, an override, a compliance clearance, an escalation, an access
        // change and, newest, a rejection — an hour apart.
        var paid = await ReadyToDisburseAsync(lending, vake, senior, null);
        Assert.True((await DisburseAsync(vake, paid)).IsSuccess);
        _clock.Advance(TimeSpan.FromHours(1));
        var failed = await ReadyToDisburseAsync(lending, vake, senior, await lending.ClientAsync(MockPersonalNumbers.CoreBankingError));
        Assert.True((await DisburseAsync(vake, failed)).IsFailure);
        _clock.Advance(TimeSpan.FromHours(1));
        var overridden = await lending.InReviewAsync(LendingSeedCatalog.Consumer, 10_000m, vake);
        await FailLatestScoringAsync(overridden);
        AssertSuccess(await AsAsync(senior, sp => sp.GetRequiredService<DecisionService>().ApproveAsync(overridden, [], "Long-standing employer.", Ct)));
        _clock.Advance(TimeSpan.FromHours(1));
        var cleared = await lending.InReviewAsync(LendingSeedCatalog.Consumer, 10_000m, vake);
        await SetStatusAsync(cleared, ApplicationStatus.ComplianceReview, _clock.GetUtcNow());
        AssertSuccess(await AsAsync(await UserAsync(ComplianceOfficer), sp => sp.GetRequiredService<DecisionService>()
            .DecideComplianceAsync(cleared, true, "Different date of birth.", Ct)));
        _clock.Advance(TimeSpan.FromHours(1));
        var escalated = await lending.InReviewAsync(LendingSeedCatalog.Consumer, 10_000m, vake);
        await Host.QueryAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE LoanApplications SET EscalatedAt = {_clock.GetUtcNow()} WHERE Id = {escalated.Value}", Ct));
        _clock.Advance(TimeSpan.FromHours(1));
        var deactivated = await UserAsync(BatumiOperator);
        AssertSuccess(await AsAsync(await UserAsync(InfrastructureHost.AdminUserName), sp => sp.GetRequiredService<UserAdministrationService>()
            .DeactivateAsync(deactivated, Ct)));
        _clock.Advance(TimeSpan.FromHours(1));
        var rejected = await lending.InReviewAsync(LendingSeedCatalog.Consumer, 10_000m, vake);
        AssertSuccess(await AsAsync(await UserAsync(Underwriter), sp => sp.GetRequiredService<DecisionService>()
            .RejectAsync(rejected, "Income could not be verified.", Ct)));

        var feed = (await DashboardAsync(await UserAsync(Auditor))).Attention.ToList();

        Assert.Equal(DashboardMetric.RecentRejections, feed[0].Reason);
        Assert.Equal(rejected.Value.ToString(), feed[0].Id);
        Assert.Equal("Income could not be verified.", feed[0].Note);
        Assert.Equal("Nika Beridze", feed[0].Actor);
        Assert.True(
            feed.FindIndex(i => i.Reason == DashboardMetric.RecentRejections)
            < feed.FindIndex(i => i.Reason == DashboardMetric.LargeDisbursements), "The newer rejection must come before the older payout.");
        Assert.Equal(feed.OrderByDescending(i => i.At).Select(i => i.Id), feed.Select(i => i.Id));
        Assert.All(
            new[]
            {
                DashboardMetric.RecentOverrides, DashboardMetric.RecentRejections, DashboardMetric.ComplianceClearances,
                DashboardMetric.LargeDisbursements, DashboardMetric.PayoutIncidents, DashboardMetric.RecentEscalations,
                DashboardMetric.RecentAccessChanges,
            },
            kind => Assert.Contains(feed, i => i.Reason == kind));
        Assert.Contains(feed, i => i.Reason == DashboardMetric.RecentOverrides && i.Id == overridden.Value.ToString() && i.Note == "Long-standing employer.");
        Assert.Contains(feed, i => i.Reason == DashboardMetric.ComplianceClearances && i.Id == cleared.Value.ToString() && i.Note == "Different date of birth.");
        Assert.Contains(feed, i => i.Reason == DashboardMetric.PayoutIncidents && i.Id == failed.Value.ToString());
        Assert.Contains(feed, i => i.Reason == DashboardMetric.LargeDisbursements && i.Id == paid.Value.ToString() && i.AmountGel == 10_000m);
        Assert.Contains(feed, i => i.Reason == DashboardMetric.RecentEscalations && i.Id == escalated.Value.ToString() && i.Actor is null);
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
        Assert.Contains(dashboard.Attention, i => i.Reason == DashboardMetric.RecentAccessChanges); // accounts created by the seed
        Assert.Equal(dashboard.Attention.OrderByDescending(i => i.At).Select(i => i.Id), dashboard.Attention.Select(i => i.Id)); // one feed, newest first
        Assert.DoesNotContain(operatorDashboard.Tiles, t => t.Metric == DashboardMetric.LockedUsers);
    }

    private static int Count(DashboardData data, DashboardMetric metric) => data.Tiles.Single(t => t.Metric == metric).Count;

    private static void AssertSuccess(Result result) => Assert.True(result.IsSuccess, result.Error?.Description);

    private Task<T> AsAsync<T>(UserId user, Func<IServiceProvider, Task<T>> call) => Host.InScopeAsAsync(user, call);

    private Task<Result<DisbursementStatus>> DisburseAsync(UserId user, LoanApplicationId id) =>
        AsAsync(user, sp => sp.GetRequiredService<DisbursementService>().DisburseAsync(id, Ct));

    /// <summary>An approved consumer loan of 10,000 GEL, signed and with its conditions met.</summary>
    private async Task<LoanApplicationId> ReadyToDisburseAsync(TestLending lending, UserId operatorId, UserId senior, ClientId? borrower)
    {
        var id = await lending.ApprovedAsync(LendingSeedCatalog.Consumer, 10_000m, operatorId, [senior], borrower);
        AssertSuccess(await AsAsync(operatorId, sp => sp.GetRequiredService<ContractService>().SignAsync(id, Ct)));
        var conditions = await Host.QueryAsync(db => db.LoanApplications.Where(a => a.Id == id)
            .SelectMany(a => a.Conditions).Select(c => c.Id).ToListAsync(Ct));
        foreach (var condition in conditions)
        {
            AssertSuccess(await AsAsync(operatorId, sp => sp.GetRequiredService<ContractService>().MarkConditionMetAsync(id, condition, Ct)));
        }

        return id;
    }

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
