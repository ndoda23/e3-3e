using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Common;

/// <summary>Saves tracked changes and runs several writes as one database transaction.</summary>
public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyConflictException">The aggregate was changed by someone else since it was loaded.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> inside a transaction. The transaction is committed when the returned result is a
    /// success and rolled back when it is a failure or an exception is thrown.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
        where TResult : Result;
}

/// <summary>
/// Another user changed the same aggregate after it was loaded (optimistic concurrency, <c>rowversion</c>). Nothing was
/// saved; the operation may be retried with fresh data.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
