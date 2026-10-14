using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.Disbursements;

public readonly record struct DisbursementId(Guid Value)
{
    public static DisbursementId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}

public enum DisbursementStatus
{
    /// <summary>Committed before core banking is called; the outcome is not known yet (or the answer timed out).</summary>
    Pending = 1,

    /// <summary>Core banking confirmed the transfer.</summary>
    Succeeded = 2,

    /// <summary>Core banking rejected the transfer (no money moved); a retry uses the same idempotency key.</summary>
    Failed = 3,
}

/// <summary>
/// The payout of one application. It is saved as Pending <b>before</b> core banking is called, with an idempotency key
/// that is the same for every attempt of the application (<see cref="KeyFor"/>): a double click, a retry after an error
/// or a retry after a timeout reaches core banking with the same key and can never pay twice. One row per application
/// (unique index), so two parallel requests cannot both create one.
/// </summary>
public sealed class Disbursement : IHasDomainEvents
{
    public const int TransactionIdMaxLength = 64;
    public const int ErrorMaxLength = 500;

    private readonly List<IDomainEvent> _events = [];

    private Disbursement()
    {
    }

    public DisbursementId Id { get; private set; }

    public LoanApplicationId ApplicationId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>The amount transferred to the client: the principal less the upfront fee.</summary>
    public Money Amount { get; private set; }

    public DisbursementStatus Status { get; private set; }

    public string? TransactionId { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public UserId RequestedBy { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    /// <summary>The idempotency key of an application's payout: deterministic, so every attempt shares it.</summary>
    public static string KeyFor(LoanApplicationId applicationId) => $"disb:{applicationId.Value:N}";

    public static Disbursement Request(LoanApplicationId applicationId, Money amount, UserId by, DateTimeOffset at)
    {
        if (!amount.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The payout must be positive.");
        }

        var disbursement = new Disbursement
        {
            Id = DisbursementId.New(),
            ApplicationId = applicationId,
            IdempotencyKey = KeyFor(applicationId),
            Amount = amount,
            Status = DisbursementStatus.Pending,
            Attempts = 1,
            RequestedBy = by,
            RequestedAt = at,
        };
        disbursement._events.Add(new DisbursementRequested(disbursement.Id, applicationId, amount, at));
        return disbursement;
    }

    /// <summary>A new attempt after a rejection, with the same key.</summary>
    public Result Retry(UserId by, DateTimeOffset at)
    {
        if (Status != DisbursementStatus.Failed)
        {
            return Result.Failure(DisbursementErrors.NotRetryable(Status));
        }

        Status = DisbursementStatus.Pending;
        Attempts++;
        RequestedBy = by;
        RequestedAt = at;
        LastError = null;
        _events.Add(new DisbursementRequested(Id, ApplicationId, Amount, at));
        return Result.Success();
    }

    /// <summary>Core banking confirmed the transfer. Idempotent for the same transaction id.</summary>
    public Result Succeed(string transactionId, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        if (Status == DisbursementStatus.Succeeded)
        {
            return transactionId == TransactionId
                ? Result.Success()
                : Result.Failure(DisbursementErrors.DifferentTransaction);
        }

        if (Status != DisbursementStatus.Pending)
        {
            return Result.Failure(DisbursementErrors.NotPending(Status));
        }

        Status = DisbursementStatus.Succeeded;
        TransactionId = transactionId;
        CompletedAt = at;
        LastError = null;
        _events.Add(new DisbursementSucceeded(Id, ApplicationId, transactionId, at));
        return Result.Success();
    }

    /// <summary>Core banking rejected the transfer: no money moved.</summary>
    public Result Fail(string error, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        if (Status != DisbursementStatus.Pending)
        {
            return Result.Failure(DisbursementErrors.NotPending(Status));
        }

        Status = DisbursementStatus.Failed;
        LastError = error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];
        CompletedAt = at;
        _events.Add(new DisbursementFailed(Id, ApplicationId, LastError, at));
        return Result.Success();
    }

    /// <summary>Core banking did not answer in time: the outcome is unknown, the payout stays Pending and is retried.</summary>
    public Result TimedOut(DateTimeOffset at)
    {
        if (Status != DisbursementStatus.Pending)
        {
            return Result.Failure(DisbursementErrors.NotPending(Status));
        }

        LastError = "Core banking did not answer in time.";
        _events.Add(new DisbursementTimedOut(Id, ApplicationId, at));
        return Result.Success();
    }

    public void ClearDomainEvents() => _events.Clear();
}

/// <summary>A payout was requested (first attempt or retry).</summary>
public sealed record DisbursementRequested(
    DisbursementId DisbursementId, LoanApplicationId ApplicationId, Money Amount, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Core banking confirmed the payout.</summary>
public sealed record DisbursementSucceeded(
    DisbursementId DisbursementId, LoanApplicationId ApplicationId, string TransactionId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Core banking rejected the payout.</summary>
public sealed record DisbursementFailed(
    DisbursementId DisbursementId, LoanApplicationId ApplicationId, string Error, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Core banking did not answer in time; the background retry asks again with the same key.</summary>
public sealed record DisbursementTimedOut(
    DisbursementId DisbursementId, LoanApplicationId ApplicationId, DateTimeOffset OccurredAt) : IDomainEvent;

public static class DisbursementErrors
{
    public static readonly Error DifferentTransaction = Error.Conflict(
        "Disbursement.DifferentTransaction", "Core banking reported a different transaction for the same payout.");

    public static readonly Error Rejected = Error.Conflict(
        "Disbursement.Rejected", "Core banking rejected the payout. No money was moved; you can try again.");

    public static readonly Error ExposureLimitExceeded = Error.Conflict(
        "Disbursement.ExposureLimitExceeded", "The payout would take a party above the exposure limit.");

    public static Error NotRetryable(DisbursementStatus status) => Error.Conflict(
        "Disbursement.NotRetryable", $"A {status} payout cannot be retried.");

    public static Error NotPending(DisbursementStatus status) => Error.Conflict(
        "Disbursement.NotPending", $"The payout is already {status}.");
}
