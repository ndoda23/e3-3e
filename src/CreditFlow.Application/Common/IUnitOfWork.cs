using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Common;

/// <summary>Saves tracked changes and runs several writes as one database transaction.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> inside a transaction. The transaction is committed when the returned result is a
    /// success and rolled back when it is a failure or an exception is thrown.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
        where TResult : Result;
}
