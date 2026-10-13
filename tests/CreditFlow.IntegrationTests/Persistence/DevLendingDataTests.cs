using CreditFlow.Application.Applications;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Loans;
using CreditFlow.Infrastructure.ExternalServices;
using CreditFlow.Infrastructure.Outbox;
using CreditFlow.Infrastructure.Persistence;
using CreditFlow.Infrastructure.Persistence.Seeding;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CreditFlow.IntegrationTests.Persistence;

/// <summary>
/// The Development demo applications: every lasting status in both branches after the live pre-check has run, data
/// that the background jobs find consistent, and a seed that runs only once.
/// </summary>
public sealed class DevLendingDataTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    private readonly FakeTimeProvider _clock = new(BankTime.ToInstant(new DateOnly(2026, 10, 13), new TimeOnly(10, 0)));

    protected override string Environment => "Development";

    protected override bool SeedDevUsers => true;

    protected override TimeProvider? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["Seed:DemoData:Enabled"] = "true",
        ["Mocks:BureauTimeout"] = "00:00:00.200",
    };

    [Fact]
    public async Task Every_lasting_status_is_seeded_in_both_branches()
    {
        await Host.Services.GetRequiredService<OutboxProcessor>().ProcessAllDueAsync(Ct);

        var applications = await Host.QueryAsync(db => db.LoanApplications.AsNoTracking()
            .Include(a => a.Decisions)
            .Join(db.Branches, a => a.BranchId, b => b.Id, (a, b) => new { a.Status, a.EscalatedAt, Branch = b.Code, Votes = a.Decisions.Count(d => d.Kind == DecisionKind.VoteApprove) })
            .ToListAsync(Ct));
        var statuses = applications.Select(a => a.Status).ToHashSet();

        ApplicationStatus[] lasting =
        [
            ApplicationStatus.Draft, ApplicationStatus.PreCheck, ApplicationStatus.ComplianceReview, ApplicationStatus.DocumentsPending,
            ApplicationStatus.Underwriting, ApplicationStatus.CreditCommittee, ApplicationStatus.CounterOffered, ApplicationStatus.Approved,
            ApplicationStatus.ConditionallyApproved, ApplicationStatus.ContractSigned, ApplicationStatus.ConditionsMet, ApplicationStatus.Active,
            ApplicationStatus.Closed, ApplicationStatus.Rejected, ApplicationStatus.Cancelled, ApplicationStatus.Expired,
        ];
        Assert.All(lasting, status => Assert.Contains(status, statuses));
        Assert.Equal([BranchSeedCatalog.Batumi, BranchSeedCatalog.Vake], applications.Select(a => a.Branch).Distinct().Order(StringComparer.Ordinal));
        Assert.Contains(applications, a => a.Status == ApplicationStatus.Underwriting && a.EscalatedAt is not null);
        Assert.Contains(applications, a => a.Status == ApplicationStatus.CreditCommittee && a.Votes == 1);
        Assert.Equal(2, applications.Count(a => a.Status == ApplicationStatus.ComplianceReview)); // sanctions and PEP
        Assert.True(applications.Count(a => a.Status == ApplicationStatus.Rejected) >= 3); // KYC, scoring rule, underwriter

        var payouts = await Host.QueryAsync(db => db.Disbursements.Select(d => d.Status).ToListAsync(Ct));
        Assert.Contains(DisbursementStatus.Pending, payouts);
        Assert.Contains(DisbursementStatus.Failed, payouts);

        var loans = await Host.QueryAsync(db => db.Loans.Include(l => l.Installments).Include(l => l.Repayments).ToListAsync(Ct));
        var today = BankTime.DateOf(_clock.GetUtcNow());
        Assert.Contains(loans, l => l.Status == LoanStatus.Active && l.Installments.Count(i => i.IsPaid) >= 3 && !l.Installments.Any(i => !i.IsPaid && i.DueDate < today));
        Assert.Contains(loans, l => l.Status == LoanStatus.Active && l.Installments.Any(i => !i.IsPaid && i.DueDate < today));
        Assert.Contains(loans, l => l.Status == LoanStatus.Closed && l.Repayments.Any(r => r.Kind == RepaymentKind.FullEarly));
        Assert.Equal(loans.Count, loans.Select(l => l.ContractNumber).Distinct().Count());
    }

    [Fact]
    public async Task The_demo_data_is_consistent_with_the_rules_the_background_jobs_apply()
    {
        await Host.Services.GetRequiredService<OutboxProcessor>().ProcessAllDueAsync(Ct);

        var (escalated, expired) = await Host.InScopeAsync(async sp =>
        {
            var jobs = sp.GetRequiredService<ApplicationLifecycleJobs>();
            return (await jobs.EscalateOverdueAsync(Ct), await jobs.ExpireOffersAsync(Ct));
        });

        // Nothing is overdue for escalation or expiry: the seeded dates match the statuses.
        Assert.Equal((0, 0), (escalated, expired));

        // The live pre-check left the bureau-down file waiting (retried in the background), never lost.
        var waiting = await Host.QueryAsync(db => db.LoanApplications
            .Where(a => a.Status == ApplicationStatus.PreCheck)
            .Join(db.Clients, a => a.BorrowerId, c => c.Id, (a, c) => c.PersonalNumber)
            .ToListAsync(Ct));
        Assert.Equal([MockPersonalNumbers.BureauDown], waiting.Select(n => n.Value));
    }

    [Fact]
    public async Task The_demo_data_is_seeded_once()
    {
        var before = await CountsAsync();

        await Host.Services.InitializeDatabaseAsync(Ct);

        Assert.Equal(before, await CountsAsync());
        Assert.True(await Host.QueryAsync(db => db.SeedHistory.AnyAsync(s => s.SeedId == DevLendingDataSeeder.SeedId, Ct)));
        Assert.Equal(24, before.Applications); // 1 draft, 5 live pre-checks, 7 decisions, 4 contracts and payouts, 3 loans, 4 endings
    }

    private Task<(int Applications, int Clients, int Loans)> CountsAsync() =>
        Host.QueryAsync(async db => (
            await db.LoanApplications.CountAsync(Ct),
            await db.Clients.CountAsync(Ct),
            await db.Loans.CountAsync(Ct)));
}

/// <summary>Outside Development no demo application is created, whatever the configuration says.</summary>
public sealed class DevLendingDataOutsideDevelopmentTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["Seed:DemoData:Enabled"] = "true",
        ["Seed:DevUsers:Enabled"] = "true",
    };

    [Fact]
    public async Task No_demo_applications_are_created()
    {
        Assert.Equal(0, await Host.QueryAsync(db => db.LoanApplications.CountAsync(Ct)));
        Assert.False(await Host.QueryAsync(db => db.SeedHistory.AnyAsync(s => s.SeedId == DevLendingDataSeeder.SeedId, Ct)));
    }
}
