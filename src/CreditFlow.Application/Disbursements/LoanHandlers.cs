using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Loans;

namespace CreditFlow.Application.Disbursements;

/// <summary>
/// Core banking did not answer a payout in time: ask again with the same idempotency key. While it still does not
/// answer, the handler throws and the outbox retries with backoff; the payout stays Pending and is never repeated.
/// </summary>
internal sealed class RetryTimedOutDisbursementHandler(DisbursementProcessor processor) : IOutboxHandler<DisbursementTimedOut>
{
    public async Task HandleAsync(DisbursementTimedOut domainEvent, CancellationToken cancellationToken) =>
        await processor.CompleteAsync(domainEvent.DisbursementId, retryInBackground: false, cancellationToken);
}

/// <summary>Disbursed → Active: the loan and its schedule are booked (once per application).</summary>
internal sealed class BookLoanHandler(
    ILoanApplicationRepository applications, ILoanRepository loans, IUnitOfWork unitOfWork, TimeProvider clock)
    : IOutboxHandler<ApplicationStatusChanged>
{
    public async Task HandleAsync(ApplicationStatusChanged domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.To != ApplicationStatus.Disbursed)
        {
            return;
        }

        var application = await applications.GetAsync(domainEvent.ApplicationId, cancellationToken);
        if (application?.Status != ApplicationStatus.Disbursed
            || await loans.GetByApplicationAsync(application.Id, cancellationToken) is not null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var loan = Loan.Book(application, now);
        if (loan.IsFailure)
        {
            throw new InvalidOperationException(loan.Error!.Description);
        }

        loans.Add(loan.Value);
        var activated = application.Activate(now);
        if (activated.IsFailure)
        {
            throw new InvalidOperationException(activated.Error!.Description);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>A loan repaid in full closes its application (Active → Closed).</summary>
internal sealed class CloseApplicationHandler(
    ILoanApplicationRepository applications, IUnitOfWork unitOfWork, TimeProvider clock) : IOutboxHandler<LoanClosed>
{
    public async Task HandleAsync(LoanClosed domainEvent, CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(domainEvent.ApplicationId, cancellationToken);
        if (application?.Status != ApplicationStatus.Active)
        {
            return;
        }

        application.Close(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
