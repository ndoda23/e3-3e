using CreditFlow.Application.Common;
using CreditFlow.Application.External;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.ExternalServices;
using CreditFlow.Infrastructure.Outbox;
using CreditFlow.Infrastructure.Persistence;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CreditFlow.IntegrationTests.Persistence;

/// <summary>
/// The transactional outbox and the pre-check: events are stored with the change, dispatched once, retried with
/// backoff when the bureau is unavailable, and claimed safely by concurrent dispatchers.
/// </summary>
public sealed class OutboxTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    private static readonly DateOnly Today = new(2026, 10, 12);
    private readonly FakeTimeProvider _clock = new(BankTime.ToInstant(Today, new TimeOnly(10, 0)));

    protected override TimeProvider? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["Mocks:BureauTimeout"] = "00:00:00.200",
        ["Mocks:HangDuration"] = "00:00:05",
    };

    private OutboxProcessor Processor => Host.Services.GetRequiredService<OutboxProcessor>();

    [Fact]
    public async Task A_clean_application_passes_the_pre_check_and_waits_for_documents()
    {
        var id = await SubmitAsync(MockPersonalNumbers.Clean);

        await Processor.ProcessAllDueAsync(Ct);

        var (application, client, reports, messages) = await Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlowDbContext>();
            var app = await db.LoanApplications.Include(a => a.History).SingleAsync(a => a.Id == id, Ct);
            return (
                app,
                await db.Clients.SingleAsync(c => c.Id == app.BorrowerId, Ct),
                await sp.GetRequiredService<IBureauReportStore>().GetAsync(id, Ct),
                await db.Set<OutboxMessage>().ToListAsync(Ct));
        });

        Assert.Equal(ApplicationStatus.DocumentsPending, application.Status);
        Assert.Equal(KycStatus.Verified, client.KycStatus);
        Assert.Equal(ScreeningStatus.Clear, client.ScreeningStatus);
        Assert.Single(reports);
        Assert.All(messages, m => Assert.NotNull(m.ProcessedAt));
        Assert.Contains(application.History, h => h.To == ApplicationStatus.PreCheck && h.By is null);
    }

    [Theory]
    [InlineData(MockPersonalNumbers.Sanctioned, ApplicationStatus.ComplianceReview, ScreeningStatus.SanctionsMatch)]
    [InlineData(MockPersonalNumbers.Pep, ApplicationStatus.ComplianceReview, ScreeningStatus.PepMatch)]
    [InlineData(MockPersonalNumbers.KycFailure, ApplicationStatus.Rejected, ScreeningStatus.Clear)]
    public async Task Pre_check_routes_by_the_mock_scenario(string personalNumber, ApplicationStatus expected, ScreeningStatus screening)
    {
        var id = await SubmitAsync(personalNumber);

        await Processor.ProcessAllDueAsync(Ct);

        var (status, clientScreening) = await Host.QueryAsync(async db =>
        {
            var app = await db.LoanApplications.SingleAsync(a => a.Id == id, Ct);
            return (app.Status, (await db.Clients.SingleAsync(c => c.Id == app.BorrowerId, Ct)).ScreeningStatus);
        });
        Assert.Equal(expected, status);
        Assert.Equal(screening, clientScreening);
    }

    [Fact]
    public async Task An_unavailable_bureau_keeps_the_application_in_pre_check_and_retries_with_backoff()
    {
        var id = await SubmitAsync(MockPersonalNumbers.BureauDown);

        await Processor.ProcessAllDueAsync(Ct);
        var first = await PendingPreCheckMessageAsync();

        Assert.Equal(ApplicationStatus.PreCheck, await StatusAsync(id));
        Assert.Equal(1, first.Attempts);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(1), first.AvailableAt);
        Assert.Contains("unavailable", first.LastError, StringComparison.OrdinalIgnoreCase);

        // Not due yet: nothing happens. Due: second attempt, the backoff doubles.
        Assert.Equal(0, await Processor.ProcessAllDueAsync(Ct));
        _clock.Advance(TimeSpan.FromMinutes(1));
        await Processor.ProcessAllDueAsync(Ct);

        var second = await PendingPreCheckMessageAsync();
        Assert.Equal(2, second.Attempts);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(2), second.AvailableAt);
        Assert.Equal(ApplicationStatus.PreCheck, await StatusAsync(id));
    }

    [Fact]
    public async Task A_bureau_that_times_out_twice_is_retried_until_it_answers()
    {
        var id = await SubmitAsync(MockPersonalNumbers.BureauSlow);

        await Processor.ProcessAllDueAsync(Ct);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await Processor.ProcessAllDueAsync(Ct);
        Assert.Equal(ApplicationStatus.PreCheck, await StatusAsync(id));

        _clock.Advance(TimeSpan.FromMinutes(2));
        await Processor.ProcessAllDueAsync(Ct);

        Assert.Equal(ApplicationStatus.DocumentsPending, await StatusAsync(id));
    }

    [Fact]
    public async Task Events_of_a_rolled_back_transaction_are_never_dispatched()
    {
        var before = await Host.QueryAsync(db => db.Set<OutboxMessage>().CountAsync(Ct));

        await Host.InScopeAsync(async sp =>
        {
            var result = await sp.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
                async token =>
                {
                    await AddSubmittedApplicationAsync(sp, MockPersonalNumbers.Clean, token);
                    return Result.Failure(Error.Conflict("Test.Rollback", "Rolled back on purpose."));
                },
                Ct);
            Assert.True(result.IsFailure);
        });

        Assert.Equal(before, await Host.QueryAsync(db => db.Set<OutboxMessage>().CountAsync(Ct)));
    }

    [Fact]
    public async Task A_redelivered_message_does_not_run_its_handlers_again()
    {
        var id = await SubmitAsync(MockPersonalNumbers.Clean);
        await Processor.ProcessAllDueAsync(Ct);
        var notifications = await NotificationCountAsync(id);
        Assert.True(notifications > 0);

        // Simulate a crash after the handlers committed but before the message was marked processed.
        await Host.QueryAsync(db => db.Set<OutboxMessage>().ExecuteUpdateAsync(
            s => s.SetProperty(m => m.ProcessedAt, (DateTimeOffset?)null).SetProperty(m => m.ClaimedUntil, (DateTimeOffset?)null), Ct));
        await Processor.ProcessAllDueAsync(Ct);

        Assert.Equal(notifications, await NotificationCountAsync(id));
        Assert.Equal(ApplicationStatus.DocumentsPending, await StatusAsync(id));
        Assert.True(await Host.QueryAsync(db => db.Set<OutboxMessage>().AllAsync(m => m.ProcessedAt != null, Ct)));
    }

    [Fact]
    public async Task Two_dispatchers_working_at_the_same_time_handle_every_message_once()
    {
        var ids = new List<LoanApplicationId>();
        for (var i = 0; i < 6; i++)
        {
            ids.Add(await SubmitAsync(MockPersonalNumbers.Clean, newClient: true));
        }

        var second = ActivatorUtilities.CreateInstance<OutboxProcessor>(Host.Services);
        await Task.WhenAll(
            Task.Run(() => Processor.ProcessAllDueAsync(Ct), Ct),
            Task.Run(() => second.ProcessAllDueAsync(Ct), Ct));
        await Processor.ProcessAllDueAsync(Ct);

        foreach (var id in ids)
        {
            Assert.Equal(ApplicationStatus.DocumentsPending, await StatusAsync(id));
        }

        var duplicates = await Host.QueryAsync(db => db.Set<NotificationRecord>()
            .GroupBy(n => new { n.ApplicationId, n.Subject, n.Channel })
            .CountAsync(g => g.Count() > 1, Ct));
        Assert.Equal(0, duplicates);
    }

    [Fact]
    public async Task Status_changes_notify_the_client_by_SMS_and_the_operator_by_email()
    {
        var id = await SubmitAsync(MockPersonalNumbers.Clean);

        await Processor.ProcessAllDueAsync(Ct);

        var notifications = await Host.QueryAsync(db => db.Set<NotificationRecord>().Where(n => n.ApplicationId == id).ToListAsync(Ct));
        Assert.Contains(notifications, n => n.Channel == NotificationChannel.Sms && n.Subject.EndsWith(": Submitted", StringComparison.Ordinal));
        Assert.Contains(notifications, n => n.Channel == NotificationChannel.Sms && n.Subject.EndsWith(": DocumentsPending", StringComparison.Ordinal));
        Assert.Contains(notifications, n => n.Channel == NotificationChannel.Email && n.Recipient == "sysadmin@pcb.ge");
        Assert.DoesNotContain(notifications, n => n.Channel == NotificationChannel.Email && n.Subject.EndsWith(": Submitted", StringComparison.Ordinal));
    }

    // ----- Helpers -----

    private int _clientCounter;

    private Task<LoanApplicationId> SubmitAsync(string personalNumber, bool newClient = false) =>
        Host.InScopeAsync(async sp =>
        {
            var id = await AddSubmittedApplicationAsync(sp, newClient ? $"0200101{1000 + Interlocked.Increment(ref _clientCounter)}" : personalNumber, Ct);
            await sp.GetRequiredService<CreditFlowDbContext>().SaveChangesAsync(Ct);
            return id;
        });

    private async Task<LoanApplicationId> AddSubmittedApplicationAsync(IServiceProvider sp, string personalNumber, CancellationToken token)
    {
        var db = sp.GetRequiredService<CreditFlowDbContext>();
        var client = Client.Register(
            PersonalNumber.Create(personalNumber).Value,
            new ClientDetails("Test", "Client", new DateOnly(1990, 1, 1), "+995 555 000000", null, "Tbilisi"),
            IdentityDocument.Create(IdentityDocumentType.IdCard, "ID" + personalNumber, new DateOnly(2020, 1, 1), new DateOnly(2030, 1, 1)).Value,
            Today).Value;
        db.Clients.Add(client);

        var product = await db.LoanProducts.Include(p => p.Versions).SingleAsync(p => p.Code == LendingSeedCatalog.Consumer, token);
        var creator = await db.StaffMembers.Select(s => s.Id).FirstAsync(token);
        var branch = await db.Branches.Select(b => b.Id).FirstAsync(token);
        var terms = LoanTerms.Create(Money.Of(5_000m, Currency.GEL), 24, 18m, ScheduleType.Annuity).Value;
        var application = LoanApplication.Create(product, client.Id, terms, branch, creator, _clock.GetUtcNow()).Value;
        var submitted = application.Submit(
            new SubmissionContext(product, ExchangeRate.Gel(Today), new Dictionary<ClientId, Client> { [client.Id] = client }, false, Today),
            creator,
            _clock.GetUtcNow());
        Assert.True(submitted.IsSuccess, submitted.Error?.Description);
        db.LoanApplications.Add(application);
        await db.SaveChangesAsync(token);
        return application.Id;
    }

    private Task<ApplicationStatus> StatusAsync(LoanApplicationId id) =>
        Host.QueryAsync(db => db.LoanApplications.Where(a => a.Id == id).Select(a => a.Status).SingleAsync(Ct));

    private Task<int> NotificationCountAsync(LoanApplicationId id) =>
        Host.QueryAsync(db => db.Set<NotificationRecord>().CountAsync(n => n.ApplicationId == id, Ct));

    private Task<OutboxMessage> PendingPreCheckMessageAsync() =>
        Host.QueryAsync(db => db.Set<OutboxMessage>().SingleAsync(m => m.ProcessedAt == null, Ct));
}
