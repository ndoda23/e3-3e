using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Common;

/// <summary>
/// Reacts to a domain event after the change that raised it was committed. Events reach handlers through the
/// transactional outbox: they are stored in the same transaction as the change and dispatched in the background, with
/// retries. A handler runs in its own transaction and is recorded as processed in that transaction, so it runs at most
/// once per event even when the dispatcher retries. Throwing (e.g. <see cref="ExternalServiceUnavailableException"/>)
/// makes the dispatcher retry later with backoff.
/// </summary>
public interface IOutboxHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>An external service did not answer (unavailable or timed out); the operation is retried later.</summary>
public sealed class ExternalServiceUnavailableException : Exception
{
    public ExternalServiceUnavailableException()
    {
    }

    public ExternalServiceUnavailableException(string message)
        : base(message)
    {
    }

    public ExternalServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
