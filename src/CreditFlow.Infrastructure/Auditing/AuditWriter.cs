using CreditFlow.Application.Auditing;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Auditing;
using CreditFlow.Infrastructure.Persistence;

namespace CreditFlow.Infrastructure.Auditing;

/// <summary>Appends explicit audit events, filling actor and request context from <see cref="ICurrentUser"/>.</summary>
internal sealed class AuditWriter(CreditFlowDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditWriter
{
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        db.AuditLogs.Add(AuditLog.Create(new AuditEntry
        {
            OccurredAt = clock.GetUtcNow(),
            Action = auditEvent.Action,
            UserId = auditEvent.UserId ?? currentUser.UserId?.Value,
            UserName = auditEvent.UserName ?? currentUser.UserName,
            EntityType = auditEvent.EntityType,
            EntityId = auditEvent.EntityId,
            BeforeJson = auditEvent.BeforeJson,
            AfterJson = auditEvent.AfterJson,
            IpAddress = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent,
            CorrelationId = currentUser.CorrelationId,
        }));

        await db.SaveChangesAsync(cancellationToken);
    }
}
