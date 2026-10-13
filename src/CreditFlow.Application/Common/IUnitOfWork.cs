using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Common;

/// <summary>Saves tracked changes and runs several writes as one database transaction.</summary>
public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyConflictException">The aggregate was changed by someone else since it was loaded.</exception>
    /// <exception cref="UniqueConstraintViolationException">A unique index rejected the change (a concurrent request won).</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Forgets every loaded entity, so the next reads see the committed state — for a step that follows a committed one
    /// and must not act on data loaded before another request committed.
    /// </summary>
    void Reset();

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

/// <summary>
/// A unique index rejected the save: another request committed the same thing first (the service's own check ran
/// before it). Nothing was saved; <see cref="IndexName"/> tells which rule (see <see cref="UniqueIndexes"/>).
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException()
    {
    }

    public UniqueConstraintViolationException(string message)
        : base(message)
    {
    }

    public UniqueConstraintViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public UniqueConstraintViolationException(string? indexName, string message, Exception innerException)
        : base(message, innerException) =>
        IndexName = indexName;

    /// <summary>The violated index, or <c>null</c> when the database did not name it.</summary>
    public string? IndexName { get; }
}
