using System.Text.Json;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Loans;
using CreditFlow.Infrastructure.Persistence;

namespace CreditFlow.Infrastructure.Outbox;

/// <summary>A domain event waiting to be dispatched (written in the same transaction as the change that raised it).</summary>
public sealed class OutboxMessage
{
    public const int TypeMaxLength = 200;
    public const int ErrorMaxLength = 2000;

    public Guid Id { get; set; }

    /// <summary>The event type key (see <see cref="OutboxEventTypes"/>).</summary>
    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Not dispatched before this time (backoff after failures).</summary>
    public DateTimeOffset AvailableAt { get; set; }

    /// <summary>A dispatcher holds the message until this time; after it, another dispatcher may take it over.</summary>
    public DateTimeOffset? ClaimedUntil { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>A handler that already processed a message (idempotency: written in the handler's transaction).</summary>
public sealed class ProcessedMessage
{
    public const int HandlerMaxLength = 300;

    public Guid MessageId { get; set; }

    public string Handler { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>The domain events that go through the outbox, with stable type keys (never rename a key).</summary>
public static class OutboxEventTypes
{
    private static readonly Dictionary<string, Type> ByKey = new(StringComparer.Ordinal)
    {
        ["ApplicationStatusChanged"] = typeof(ApplicationStatusChanged),
        ["ApplicationEscalated"] = typeof(ApplicationEscalated),
        ["ApplicationScored"] = typeof(ApplicationScored),
        ["ApplicationDecisionRecorded"] = typeof(ApplicationDecisionRecorded),
        ["DisbursementRequested"] = typeof(DisbursementRequested),
        ["DisbursementSucceeded"] = typeof(DisbursementSucceeded),
        ["DisbursementFailed"] = typeof(DisbursementFailed),
        ["DisbursementTimedOut"] = typeof(DisbursementTimedOut),
        ["LoanRepaymentRecorded"] = typeof(LoanRepaymentRecorded),
        ["LoanClosed"] = typeof(LoanClosed),
    };

    private static readonly Dictionary<Type, string> ByType = ByKey.ToDictionary(p => p.Value, p => p.Key);

    public static OutboxMessage ToMessage(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (!ByType.TryGetValue(domainEvent.GetType(), out var key))
        {
            throw new InvalidOperationException($"{domainEvent.GetType().Name} is not registered in {nameof(OutboxEventTypes)}.");
        }

        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = key,
            Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonColumns.Options),
            OccurredAt = domainEvent.OccurredAt,
            AvailableAt = domainEvent.OccurredAt,
        };
    }

    public static IDomainEvent FromMessage(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var type = ByKey.TryGetValue(message.Type, out var known)
            ? known
            : throw new InvalidOperationException($"Unknown outbox message type '{message.Type}'.");
        return (IDomainEvent)JsonSerializer.Deserialize(message.Payload, type, JsonColumns.Options)!;
    }
}
