using System.Collections.Concurrent;
using System.Text.Json;
using CreditFlow.Application.Common;
using CreditFlow.Application.External;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Scoring;
using CreditFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CreditFlow.Infrastructure.ExternalServices;

public enum SanctionsListKind
{
    Sanctions = 1,
    Pep = 2,
}

/// <summary>An entry of the (mock) sanctions / PEP list kept in the database.</summary>
public sealed class SanctionsListEntry
{
    public int Id { get; set; }

    public string? PersonalNumber { get; set; }

    public string FullName { get; set; } = string.Empty;

    public SanctionsListKind Kind { get; set; }

    /// <summary>The source list, e.g. UN, OFAC, national PEP register.</summary>
    public string ListName { get; set; } = string.Empty;
}

/// <summary>Screening against the list in the database: a match by personal number or by full name.</summary>
internal sealed class MockSanctionsScreeningService(CreditFlowDbContext db) : ISanctionsScreeningService
{
    public async Task<ScreeningStatus> ScreenAsync(Client client, CancellationToken cancellationToken)
    {
        var number = client.PersonalNumber.Value;
        var name = client.FullName;
        var kinds = await db.Set<SanctionsListEntry>().AsNoTracking()
            .Where(e => e.PersonalNumber == number || e.FullName == name)
            .Select(e => e.Kind)
            .ToListAsync(cancellationToken);

        return kinds.Contains(SanctionsListKind.Sanctions) ? ScreeningStatus.SanctionsMatch
            : kinds.Contains(SanctionsListKind.Pep) ? ScreeningStatus.PepMatch
            : ScreeningStatus.Clear;
    }
}

/// <summary>KYC: verified unless the scenario says otherwise (or the document is not valid today).</summary>
internal sealed class MockIdentityVerificationService(MockScenarios scenarios, TimeProvider clock) : IIdentityVerificationService
{
    public Task<KycStatus> VerifyAsync(Client client, CancellationToken cancellationToken)
    {
        var verified = !scenarios.Has(client.PersonalNumber.Value, MockScenario.KycFailure)
                       && client.IdentityDocument.IsValidOn(BankTime.DateOf(clock.GetUtcNow()));
        return Task.FromResult(verified ? KycStatus.Verified : KycStatus.Failed);
    }
}

/// <summary>
/// The credit bureau mock. Unavailability and timeouts are real: an unavailable bureau throws, and a slow one hangs
/// until the caller's timeout cancels the request; both surface as <see cref="ExternalServiceUnavailableException"/>.
/// </summary>
internal sealed class MockCreditBureauClient(MockScenarios scenarios, TimeProvider clock) : ICreditBureauClient
{
    /// <summary>Requests per personal number, for "time out twice, then answer" (the mock is a singleton per host).</summary>
    private readonly ConcurrentDictionary<string, int> _requests = new(StringComparer.Ordinal);

    public async Task<BureauReport> GetReportAsync(Client client, CancellationToken cancellationToken)
    {
        var number = client.PersonalNumber.Value;
        if (scenarios.Has(number, MockScenario.BureauUnavailable))
        {
            throw new ExternalServiceUnavailableException("The credit bureau is unavailable.");
        }

        if (scenarios.Has(number, MockScenario.BureauTimeoutTwice) && _requests.AddOrUpdate(number, 1, (_, n) => n + 1) <= 2)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(scenarios.Options.BureauTimeout);
            try
            {
                await Task.Delay(scenarios.Options.HangDuration, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ExternalServiceUnavailableException("The credit bureau did not answer in time.");
            }
        }

        var today = BankTime.DateOf(clock.GetUtcNow());
        IReadOnlyList<Delinquency> delinquencies = scenarios.Has(number, MockScenario.BureauDelinquency)
            ? [new Delinquency(today.AddMonths(-3), 45)]
            : [];
        IReadOnlyList<Money> obligations = scenarios.Has(number, MockScenario.BureauHighObligations)
            ? [Money.Of(2_500m, Currency.GEL)]
            : [];
        return new BureauReport(delinquencies, obligations);
    }
}

/// <summary>A bureau report stored for scoring.</summary>
public sealed class BureauReportRecord
{
    public LoanApplicationId ApplicationId { get; set; }

    public ClientId ClientId { get; set; }

    public DateTimeOffset RetrievedAt { get; set; }

    public string ReportJson { get; set; } = string.Empty;
}

internal sealed class BureauReportStore(CreditFlowDbContext db) : IBureauReportStore
{
    public async Task SaveAsync(
        LoanApplicationId applicationId, ClientId clientId, BureauReport report, DateTimeOffset retrievedAt,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(report, JsonColumns.Options);
        var existing = await db.Set<BureauReportRecord>()
            .FirstOrDefaultAsync(r => r.ApplicationId == applicationId && r.ClientId == clientId, cancellationToken);
        if (existing is null)
        {
            db.Set<BureauReportRecord>().Add(new BureauReportRecord
            {
                ApplicationId = applicationId, ClientId = clientId, RetrievedAt = retrievedAt, ReportJson = json,
            });
        }
        else
        {
            existing.RetrievedAt = retrievedAt;
            existing.ReportJson = json;
        }
    }

    public async Task<IReadOnlyDictionary<ClientId, BureauReport>> GetAsync(
        LoanApplicationId applicationId, CancellationToken cancellationToken)
    {
        var records = await db.Set<BureauReportRecord>().AsNoTracking()
            .Where(r => r.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);
        return records.ToDictionary(
            r => r.ClientId, r => JsonSerializer.Deserialize<BureauReport>(r.ReportJson, JsonColumns.Options)!);
    }
}

/// <summary>A notification written instead of being sent.</summary>
public sealed class NotificationRecord
{
    public const int RecipientMaxLength = 256;
    public const int SubjectMaxLength = 200;
    public const int BodyMaxLength = 2000;

    public long Id { get; set; }

    public NotificationChannel Channel { get; set; }

    public string Recipient { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public LoanApplicationId? ApplicationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Email / SMS mock: writes the notification to the <c>Notifications</c> table (same transaction) and the log.</summary>
internal sealed partial class DatabaseNotificationSender(
    CreditFlowDbContext db, TimeProvider clock, ILogger<DatabaseNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        db.Set<NotificationRecord>().Add(new NotificationRecord
        {
            Channel = notification.Channel,
            Recipient = Limit(notification.Recipient, NotificationRecord.RecipientMaxLength),
            Subject = Limit(notification.Subject, NotificationRecord.SubjectMaxLength),
            Body = Limit(SensitiveData.MaskText(notification.Body), NotificationRecord.BodyMaxLength),
            ApplicationId = notification.ApplicationId,
            CreatedAt = clock.GetUtcNow(),
        });
        LogNotification(notification.Channel, notification.Recipient, notification.Subject);
        return Task.CompletedTask;
    }

    private static string Limit(string text, int length) => text.Length <= length ? text : text[..length];

    [LoggerMessage(Level = LogLevel.Information, Message = "{Channel} to {Recipient}: {Subject}")]
    private partial void LogNotification(NotificationChannel channel, string recipient, string subject);
}
