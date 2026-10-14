namespace CreditFlow.Domain.Common;

/// <summary>Something that happened in the domain, published after the change is saved (through the outbox).</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>An aggregate that raises domain events; the persistence layer collects them in the same transaction.</summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
