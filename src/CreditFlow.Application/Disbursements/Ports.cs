using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Disbursements;

/// <summary>A payout to the borrower's account. Core banking executes each <see cref="IdempotencyKey"/> at most once.</summary>
public sealed record CoreBankingTransfer(string IdempotencyKey, string BeneficiaryPersonalNumber, Money Amount, string Reference);

/// <summary>The answer of core banking: the transaction id, or why the transfer was rejected (no money moved).</summary>
public sealed record CoreBankingResult(string? TransactionId, string? RejectionReason)
{
    public bool Accepted => TransactionId is not null;

    public static CoreBankingResult Success(string transactionId) => new(transactionId, null);

    public static CoreBankingResult Rejected(string reason) => new(null, reason);
}

/// <summary>
/// Core banking (mocked). A call with a key it has already executed returns the original transaction id. When it does
/// not answer in time it throws <see cref="Common.ExternalServiceUnavailableException"/>: the transfer may or may not
/// have happened, so the caller retries later with the same key.
/// </summary>
public interface ICoreBankingClient
{
    Task<CoreBankingResult> TransferAsync(CoreBankingTransfer transfer, CancellationToken cancellationToken);
}

/// <summary>
/// Gapless contract numbers: the next number of the year from a locked counter row, inside the caller's transaction,
/// so a rolled-back signing gives its number back.
/// </summary>
public interface IContractNumberGenerator
{
    /// <summary>Must be called inside a transaction (<see cref="Common.IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>).</summary>
    Task<int> NextAsync(int year, CancellationToken cancellationToken);
}

/// <summary>
/// Serializes exposure checks per client: an exclusive application lock per party (<c>sp_getapplock</c>), held until
/// the transaction ends and taken in a fixed order (no deadlocks). Two payouts that share a party run one after the
/// other, so they cannot both pass an exposure check that only one of them fits.
/// </summary>
public interface IExposureLock
{
    /// <summary>Must be called inside a transaction.</summary>
    Task AcquireAsync(IReadOnlyCollection<ClientId> clients, CancellationToken cancellationToken);
}
