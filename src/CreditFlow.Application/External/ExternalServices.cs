using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Scoring;

namespace CreditFlow.Application.External;

/// <summary>Sanctions and PEP list screening (mock: a list in the database).</summary>
public interface ISanctionsScreeningService
{
    Task<ScreeningStatus> ScreenAsync(Client client, CancellationToken cancellationToken);
}

/// <summary>KYC: verifies the identity document data entered by the operator (mock).</summary>
public interface IIdentityVerificationService
{
    Task<KycStatus> VerifyAsync(Client client, CancellationToken cancellationToken);
}

/// <summary>
/// The credit bureau: late payments and obligations at other banks (mock). Throws
/// <see cref="Common.ExternalServiceUnavailableException"/> when the bureau is down or does not answer in time.
/// </summary>
public interface ICreditBureauClient
{
    Task<BureauReport> GetReportAsync(Client client, CancellationToken cancellationToken);
}

/// <summary>Keeps the bureau reports fetched during pre-check for scoring.</summary>
public interface IBureauReportStore
{
    Task SaveAsync(LoanApplicationId applicationId, ClientId clientId, BureauReport report, DateTimeOffset retrievedAt, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<ClientId, BureauReport>> GetAsync(LoanApplicationId applicationId, CancellationToken cancellationToken);
}

public enum NotificationChannel
{
    Email = 1,
    Sms = 2,
}

/// <summary>A message to a client or staff member.</summary>
public sealed record Notification(
    NotificationChannel Channel, string Recipient, string Subject, string Body, LoanApplicationId? ApplicationId);

/// <summary>Email / SMS. The mock writes notifications to the database and the log instead of sending them.</summary>
public interface INotificationSender
{
    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}
