using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Scoring;

namespace CreditFlow.Application.Disbursements;

/// <summary>
/// Step 8: the operator requests the payout and the system executes it, idempotently.
/// <list type="number">
/// <item><b>Transaction:</b> the application is loaded through the viewer's access filter; an exclusive lock is taken
/// on every party (<see cref="IExposureLock"/>); a payout that already exists is reused (a double click or a second
/// browser finds it here); otherwise the guards run (signed contract, all conditions met, identity documents valid
/// today), the exposure of every party is re-checked against the limit of the rule set the application was scored
/// with, and the payout is saved as <see cref="DisbursementStatus.Pending"/> with its idempotency key — before any
/// money moves.</item>
/// <item><b>Core banking</b> is called with that key (<see cref="DisbursementProcessor"/>): success → Disbursed (the
/// loan is booked in the background); rejection → Failed, a retry uses the same key; no answer → stays Pending and is
/// retried in the background with the same key, which returns the original transaction id.</item>
/// </list>
/// </summary>
public sealed class DisbursementService(
    CurrentAccessProfile access,
    ILoanApplicationRepository applications,
    IDisbursementRepository disbursements,
    IClientRepository clients,
    IExposureLock exposureLock,
    IExposureQueries exposure,
    IScoringRuleSetRepository ruleSets,
    DisbursementProcessor processor,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<Result<DisbursementStatus>> DisburseAsync(LoanApplicationId id, CancellationToken cancellationToken)
    {
        DisbursementId? payout = null;
        var prepared = await ApplicationChange.RunAsync(
            access, applications, unitOfWork, clock, id, Permissions.Loans.Disburse,
            async (application, profile, now) =>
            {
                var partyIds = application.Parties.Select(p => p.ClientId).ToList();
                await exposureLock.AcquireAsync(partyIds, cancellationToken);

                // Read only under the lock: a parallel request for the same application has committed by now.
                var existing = await disbursements.GetByApplicationAsync(application.Id, cancellationToken);
                if (existing is { Status: DisbursementStatus.Pending or DisbursementStatus.Succeeded })
                {
                    payout = existing.Id;
                    return Result.Success();
                }

                var parties = await clients.GetManyAsync(partyIds, cancellationToken);
                var requested = application.RequestDisbursement(parties, BankTime.DateOf(now));
                if (requested.IsFailure)
                {
                    return requested;
                }

                var withinLimit = await WithinExposureLimitAsync(application, partyIds, cancellationToken);
                if (withinLimit.IsFailure)
                {
                    return withinLimit;
                }

                if (existing is not null)
                {
                    var retried = existing.Retry(profile.UserId, now);
                    if (retried.IsFailure)
                    {
                        return retried;
                    }

                    payout = existing.Id;
                }
                else
                {
                    var amount = application.Terms.Amount - application.Terms.Amount.Percent(application.Snapshot!.Terms.UpfrontFeePercent);
                    var created = Disbursement.Request(application.Id, amount, profile.UserId, now);
                    disbursements.Add(created);
                    payout = created.Id;
                }

                return Result.Success();
            },
            cancellationToken);

        return prepared.IsFailure
            ? Result.Failure<DisbursementStatus>(prepared.Error!)
            : await processor.CompleteAsync(payout!.Value, retryInBackground: true, cancellationToken);
    }

    /// <summary>
    /// Every party's committed exposure (payouts pending or done, active loans) plus this loan stays within the limit.
    /// Runs under the parties' locks, so a parallel payout of another application is either fully counted or waits.
    /// </summary>
    private async Task<Result> WithinExposureLimitAsync(
        LoanApplication application, IReadOnlyCollection<ClientId> partyIds, CancellationToken cancellationToken)
    {
        var ruleSet = application.LatestScoring is { } scoring
            ? await ruleSets.GetAsync(scoring.RuleSetVersion, cancellationToken)
            : await ruleSets.GetLatestAsync(cancellationToken);
        if (ruleSet is null || ExposureLimitRule.LimitOf(ruleSet) is not { } limit)
        {
            return Result.Success();
        }

        var committed = await exposure.CommittedExposureGelAsync(partyIds, application.Id, cancellationToken);
        var amountGel = application.AmountGel ?? application.Terms.Amount.Amount;
        return partyIds.All(p => committed.GetValueOrDefault(p) + amountGel <= limit)
            ? Result.Success()
            : Result.Failure(DisbursementErrors.ExposureLimitExceeded);
    }
}

/// <summary>
/// Executes a Pending payout at core banking with its idempotency key and records the outcome. Used by the operator's
/// request and by the background retry after a timeout; safe to run twice in parallel (the same key gives the same
/// transaction id, and the second recording loses the <c>rowversion</c> race and reads the result).
/// </summary>
public sealed class DisbursementProcessor(
    IDisbursementRepository disbursements,
    ILoanApplicationRepository applications,
    IClientRepository clients,
    ICoreBankingClient coreBanking,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <param name="id">The payout.</param>
    /// <param name="retryInBackground">On a timeout: record it so the outbox retries (an operator's request), or
    /// throw so the outbox message itself is retried with backoff (the background retry).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Result<DisbursementStatus>> CompleteAsync(
        DisbursementId id, bool retryInBackground, CancellationToken cancellationToken)
    {
        unitOfWork.Reset(); // a parallel request may have committed since this request loaded anything
        var disbursement = await disbursements.GetAsync(id, cancellationToken)
                           ?? throw new InvalidOperationException($"Disbursement {id} does not exist.");
        if (disbursement.Status != DisbursementStatus.Pending)
        {
            return Outcome(disbursement);
        }

        var application = await applications.GetAsync(disbursement.ApplicationId, cancellationToken)
                          ?? throw new InvalidOperationException($"Application {disbursement.ApplicationId} does not exist.");
        var borrower = await clients.GetAsync(application.BorrowerId, cancellationToken)
                       ?? throw new InvalidOperationException($"Client {application.BorrowerId} does not exist.");

        CoreBankingResult answer;
        try
        {
            answer = await coreBanking.TransferAsync(
                new CoreBankingTransfer(
                    disbursement.IdempotencyKey, borrower.PersonalNumber.Value, disbursement.Amount, application.ContractNumber!),
                cancellationToken);
        }
        catch (ExternalServiceUnavailableException) when (retryInBackground)
        {
            return await RecordAsync(id, () => disbursement.TimedOut(clock.GetUtcNow()), cancellationToken);
        }

        return await RecordAsync(id, () =>
        {
            var now = clock.GetUtcNow();
            if (answer.Accepted)
            {
                var succeeded = disbursement.Succeed(answer.TransactionId!, now);
                return succeeded.IsSuccess ? application.MarkDisbursed(now) : succeeded;
            }

            var failed = disbursement.Fail(answer.RejectionReason!, now);
            return failed.IsSuccess ? application.DisbursementFailed() : failed;
        }, cancellationToken);
    }

    private static Result<DisbursementStatus> Outcome(Disbursement disbursement) =>
        disbursement.Status == DisbursementStatus.Failed
            ? Result.Failure<DisbursementStatus>(DisbursementErrors.Rejected)
            : Result.Success(disbursement.Status);

    /// <summary>Saves the outcome; when another request recorded it first, its outcome is returned instead.</summary>
    private async Task<Result<DisbursementStatus>> RecordAsync(
        DisbursementId id, Func<Result> change, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    var result = change();
                    if (result.IsSuccess)
                    {
                        await unitOfWork.SaveChangesAsync(token);
                    }

                    return result;
                },
                cancellationToken);
            if (saved.IsFailure)
            {
                return Result.Failure<DisbursementStatus>(saved.Error!);
            }
        }
        catch (Exception exception) when (exception is ConcurrencyConflictException or UniqueConstraintViolationException)
        {
            // Someone else recorded the outcome in the meantime; read it.
        }

        var current = await disbursements.GetAsync(id, cancellationToken)
                      ?? throw new InvalidOperationException($"Disbursement {id} does not exist.");
        return Outcome(current);
    }
}
