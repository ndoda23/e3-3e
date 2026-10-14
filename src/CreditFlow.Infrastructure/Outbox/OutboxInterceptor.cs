using CreditFlow.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CreditFlow.Infrastructure.Outbox;

/// <summary>
/// The transactional outbox: the domain events of every tracked aggregate are turned into <see cref="OutboxMessage"/>
/// rows inside the same SaveChanges, so they are committed (or rolled back) together with the change.
/// </summary>
public sealed class OutboxInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var sources = context.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        foreach (var source in sources)
        {
            context.AddRange(source.DomainEvents.Select(OutboxEventTypes.ToMessage));
            source.ClearDomainEvents();
        }
    }
}
