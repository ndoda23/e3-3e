using CreditFlow.Application.Applications;
using CreditFlow.Application.Common;
using CreditFlow.Application.External;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.ExternalServices;
using CreditFlow.Infrastructure.Outbox;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CreditFlow.IntegrationTests.Persistence;

/// <summary>
/// Escalation and offer expiry on SQL Server: the deadline in working days (weekends and holidays skipped), and
/// idempotency — a second run or a parallel runner never repeats the effect.
/// </summary>
public sealed class LifecycleJobsTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    private const string VakeOperator = "anna.gelashvili";

    // Friday 9 October 2026, 10:00 in Tbilisi.
    private readonly FakeTimeProvider _clock = new(BankTime.ToInstant(new DateOnly(2026, 10, 9), new TimeOnly(10, 0)));
    private TestLending? _lending;

    protected override string Environment => "Development";

    protected override bool SeedDevUsers => true;

    protected override TimeProvider? Clock => _clock;

    private TestLending Lending => _lending ??= new TestLending(Host.Services, _clock);

    // ----- Escalation -----

    [Fact]
    public async Task A_file_from_friday_is_escalated_on_tuesday_at_the_same_time_and_only_once()
    {
        var id = await InUnderwritingAsync();

        SetBankTime(2026, 10, 12, new TimeOnly(18, 0)); // Monday evening: one working day has passed
        Assert.Equal(0, await EscalateAsync());

        SetBankTime(2026, 10, 13, new TimeOnly(10, 0)); // Tuesday 10:00: two working days
        Assert.Equal(1, await EscalateAsync());
        Assert.Equal(0, await EscalateAsync());

        var application = await Host.QueryAsync(db => db.LoanApplications.SingleAsync(a => a.Id == id, Ct));
        Assert.Equal(_clock.GetUtcNow(), application.EscalatedAt);
        Assert.Equal(ApplicationStatus.Underwriting, application.Status);
        Assert.Equal(1, await AuditCountAsync(id, AuditActions.ApplicationEscalated));
    }

    [Fact]
    public async Task Public_holidays_do_not_count()
    {
        SetBankTime(2026, 10, 13, new TimeOnly(10, 0)); // Tuesday; Wednesday 14 October is Svetitskhovloba
        var id = await InUnderwritingAsync();

        SetBankTime(2026, 10, 15, new TimeOnly(11, 0)); // Thursday: one working day only
        Assert.Equal(0, await EscalateAsync());

        SetBankTime(2026, 10, 16, new TimeOnly(10, 0)); // Friday 10:00
        Assert.Equal(1, await EscalateAsync());
        Assert.NotNull(await Host.QueryAsync(db => db.LoanApplications.Where(a => a.Id == id).Select(a => a.EscalatedAt).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Two_runners_at_the_same_time_escalate_each_file_once()
    {
        var ids = new List<LoanApplicationId>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await InUnderwritingAsync());
        }

        SetBankTime(2026, 10, 14, new TimeOnly(12, 0));
        var counts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(EscalateAsync, Ct)));

        Assert.Equal(5, counts.Sum());
        foreach (var id in ids)
        {
            Assert.Equal(1, await AuditCountAsync(id, AuditActions.ApplicationEscalated));
        }
    }

    [Fact]
    public async Task An_escalated_file_leaves_the_underwriters_queue_and_the_seniors_are_notified()
    {
        var id = await InUnderwritingAsync();
        SetBankTime(2026, 10, 14, new TimeOnly(12, 0));
        await EscalateAsync();
        await Host.Services.GetRequiredService<OutboxProcessor>().ProcessAllDueAsync(Ct);

        var underwriter = await UserAsync("nika.beridze");
        var senior = await UserAsync("tamta.tatishvili");
        Assert.DoesNotContain((await InboxAsync(underwriter)).Items, i => i.Id == id);
        Assert.Contains((await InboxAsync(senior)).Items, i => i.Id == id);
        Assert.Equal(ApplicationErrors.AuthorityTooLow, (await Host.InScopeAsAsync(underwriter, sp =>
            sp.GetRequiredService<DecisionService>().ApproveAsync(id, [], null, Ct))).Error);

        var recipients = (await Host.QueryAsync(db => db.Set<NotificationRecord>().Where(n => n.ApplicationId == id).ToListAsync(Ct)))
            .Where(n => n.Subject.EndsWith(": escalated", StringComparison.Ordinal))
            .Select(n => n.Recipient)
            .ToList();
        Assert.Equal(["mariam.l@pcb.ge", "tamta.t@pcb.ge"], recipients.Order(StringComparer.Ordinal));
        Assert.All(recipients, r => Assert.DoesNotContain("nika", r, StringComparison.Ordinal));
    }

    // ----- Offer expiry -----

    [Fact]
    public async Task An_offer_expires_after_thirty_days_once()
    {
        var id = await ApprovedAsync();

        _clock.Advance(TimeSpan.FromDays(LendingPolicy.OfferValidityDays) - TimeSpan.FromMinutes(1));
        Assert.Equal(0, await ExpireAsync());

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await ExpireAsync());
        Assert.Equal(0, await ExpireAsync());

        var application = await Host.QueryAsync(db => db.LoanApplications.Include(a => a.History).SingleAsync(a => a.Id == id, Ct));
        Assert.Equal(ApplicationStatus.Expired, application.Status);
        Assert.Single(application.History, h => h.To == ApplicationStatus.Expired);
        Assert.False(application.IsOpen);
    }

    [Fact]
    public async Task Two_runners_at_the_same_time_expire_each_offer_once()
    {
        var ids = new List<LoanApplicationId>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await ApprovedAsync());
        }

        _clock.Advance(TimeSpan.FromDays(LendingPolicy.OfferValidityDays + 1));
        var counts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(ExpireAsync, Ct)));

        Assert.Equal(5, counts.Sum());
        var history = await Host.QueryAsync(db => db.LoanApplications
            .SelectMany(a => a.History).CountAsync(h => h.To == ApplicationStatus.Expired, Ct));
        Assert.Equal(5, history);
    }

    [Fact]
    public async Task The_client_is_told_that_the_offer_expired()
    {
        var id = await ApprovedAsync(await Lending.ClientAsync(MockPersonalNumbers.Clean));
        _clock.Advance(TimeSpan.FromDays(LendingPolicy.OfferValidityDays + 1));

        await ExpireAsync();
        await Host.Services.GetRequiredService<OutboxProcessor>().ProcessAllDueAsync(Ct);

        var notifications = await Host.QueryAsync(db => db.Set<NotificationRecord>().Where(n => n.ApplicationId == id).ToListAsync(Ct));
        Assert.Contains(notifications, n => n.Channel == NotificationChannel.Sms && n.Subject.EndsWith(": Expired", StringComparison.Ordinal));
        Assert.Contains(notifications, n => n.Channel == NotificationChannel.Email && n.Subject.EndsWith(": Expired", StringComparison.Ordinal));
    }

    // ----- Helpers -----

    private void SetBankTime(int year, int month, int day, TimeOnly time) =>
        _clock.SetUtcNow(BankTime.ToInstant(new DateOnly(year, month, day), time));

    private Task<int> EscalateAsync() =>
        Host.InScopeAsync(sp => sp.GetRequiredService<ApplicationLifecycleJobs>().EscalateOverdueAsync(Ct));

    private Task<int> ExpireAsync() =>
        Host.InScopeAsync(sp => sp.GetRequiredService<ApplicationLifecycleJobs>().ExpireOffersAsync(Ct));

    private Task<PagedResult<ApplicationListItem>> InboxAsync(UserId user) =>
        Host.InScopeAsAsync(user, sp => sp.GetRequiredService<DecisionService>().InboxAsync(Inbox.Underwriting, 1, Ct));

    private Task<UserId> UserAsync(string userName) =>
        Host.QueryAsync(async db => new UserId(
            await db.UserDirectory.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(Ct)));

    private Task<int> AuditCountAsync(LoanApplicationId id, string action) =>
        Host.QueryAsync(db => db.AuditLogs.CountAsync(a => a.EntityId == id.ToString() && a.Action == action, Ct));

    private async Task<LoanApplicationId> InUnderwritingAsync() =>
        await Lending.InReviewAsync(LendingSeedCatalog.Consumer, 10_000m, await UserAsync(VakeOperator));

    private async Task<LoanApplicationId> ApprovedAsync(Domain.Clients.ClientId? borrower = null) =>
        await Lending.ApprovedAsync(
            LendingSeedCatalog.Consumer, 10_000m, await UserAsync(VakeOperator), [await UserAsync("tamta.tatishvili")], borrower);
}
