using CreditFlow.Application.Common;
using CreditFlow.Application.External;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;

namespace CreditFlow.Application.Applications;

/// <summary>Step 2 starts as soon as an application is submitted: Submitted → PreCheck.</summary>
internal sealed class StartPreCheckHandler(
    ILoanApplicationRepository applications, IUnitOfWork unitOfWork, TimeProvider clock)
    : IOutboxHandler<ApplicationStatusChanged>
{
    public async Task HandleAsync(ApplicationStatusChanged domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.To != ApplicationStatus.Submitted)
        {
            return;
        }

        var application = await applications.GetAsync(domainEvent.ApplicationId, cancellationToken);
        if (application?.Status != ApplicationStatus.Submitted)
        {
            return; // already moved on (e.g. cancelled)
        }

        application.StartPreCheck(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// The pre-check (step 2) for every party: KYC, sanctions / PEP screening and the credit bureau report. A failed KYC
/// rejects the application; a list match sends it to compliance; otherwise documents are next. When the bureau is
/// unavailable the handler throws, nothing is saved, and the outbox retries later: the application waits in PreCheck
/// and is never lost.
/// </summary>
internal sealed class RunPreCheckHandler(
    ILoanApplicationRepository applications,
    IClientRepository clients,
    IIdentityVerificationService identityVerification,
    ISanctionsScreeningService screening,
    ICreditBureauClient bureau,
    IBureauReportStore bureauReports,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IOutboxHandler<ApplicationStatusChanged>
{
    public async Task HandleAsync(ApplicationStatusChanged domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.To != ApplicationStatus.PreCheck)
        {
            return;
        }

        var application = await applications.GetAsync(domainEvent.ApplicationId, cancellationToken);
        if (application?.Status != ApplicationStatus.PreCheck)
        {
            return;
        }

        var parties = await clients.GetManyAsync(application.Parties.Select(p => p.ClientId), cancellationToken);
        var now = clock.GetUtcNow();
        var checks = new List<(Client Client, KycStatus Kyc, ScreeningStatus Screening)>();
        foreach (var client in parties.Values)
        {
            var kyc = await identityVerification.VerifyAsync(client, cancellationToken);
            var listed = await screening.ScreenAsync(client, cancellationToken);
            client.RecordChecks(kyc, listed, now);
            checks.Add((client, kyc, listed));
        }

        if (checks.FirstOrDefault(c => c.Kyc == KycStatus.Failed).Client is { } unverified)
        {
            application.CompletePreCheck(PreCheckOutcome.KycFailed, $"The identity of {unverified.FullName} could not be verified.", now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        // Unavailable or timed out → ExternalServiceUnavailableException → retried later.
        foreach (var client in parties.Values)
        {
            var report = await bureau.GetReportAsync(client, cancellationToken);
            await bureauReports.SaveAsync(application.Id, client.Id, report, now, cancellationToken);
        }

        var match = checks.FirstOrDefault(c => c.Screening is ScreeningStatus.SanctionsMatch or ScreeningStatus.PepMatch);
        if (match.Client is { } listedClient)
        {
            var list = match.Screening == ScreeningStatus.SanctionsMatch ? "sanctions" : "PEP";
            application.CompletePreCheck(PreCheckOutcome.ScreeningMatch, $"{listedClient.FullName} matches the {list} list.", now);
        }
        else
        {
            application.CompletePreCheck(PreCheckOutcome.Clear, "KYC, screening and bureau checks passed.", now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Notifications on status changes (written to the database and the log by the mock sender): the operator who created
/// the application by email for every change they did not make themselves, and the borrower by SMS for the changes a
/// client cares about.
/// </summary>
internal sealed class NotifyStatusChangeHandler(
    ILoanApplicationRepository applications, IClientRepository clients, IUserDirectory users, INotificationSender sender)
    : IOutboxHandler<ApplicationStatusChanged>
{
    private static readonly HashSet<ApplicationStatus> ClientVisible =
    [
        ApplicationStatus.Submitted, ApplicationStatus.DocumentsPending, ApplicationStatus.Rejected,
        ApplicationStatus.CounterOffered, ApplicationStatus.Approved, ApplicationStatus.ConditionallyApproved,
        ApplicationStatus.Expired, ApplicationStatus.Disbursed, ApplicationStatus.Closed, ApplicationStatus.Cancelled,
    ];

    public async Task HandleAsync(ApplicationStatusChanged domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.From is null)
        {
            return; // a new draft
        }

        var application = await applications.GetAsync(domainEvent.ApplicationId, cancellationToken);
        if (application is null)
        {
            return;
        }

        var reference = ShortReference(application.Id);
        var subject = $"Application {reference}: {domainEvent.To}";

        if (domainEvent.By != application.CreatedBy
            && await users.GetDetailsAsync(application.CreatedBy, cancellationToken) is { } creator)
        {
            var body = $"Application {reference} moved from {domainEvent.From} to {domainEvent.To}."
                       + (domainEvent.Reason is { } reason ? $" Reason: {reason}" : string.Empty);
            await sender.SendAsync(new Notification(NotificationChannel.Email, creator.Email, subject, body, application.Id), cancellationToken);
        }

        if (ClientVisible.Contains(domainEvent.To)
            && await clients.GetAsync(application.BorrowerId, cancellationToken) is { } borrower)
        {
            var body = $"Dear {borrower.FirstName}, your loan application {reference} is now: {domainEvent.To}.";
            await sender.SendAsync(new Notification(NotificationChannel.Sms, borrower.Phone, subject, body, application.Id), cancellationToken);
        }
    }

    internal static string ShortReference(LoanApplicationId id) => id.Value.ToString("N")[..8].ToUpperInvariant();
}
