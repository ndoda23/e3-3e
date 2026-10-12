namespace CreditFlow.Application.Auditing;

/// <summary>
/// An explicit audit event (security events and administrative actions). Data changes are audited automatically
/// when they are saved; use this for events that are not plain data changes.
/// </summary>
public sealed record AuditEvent(string Action)
{
    /// <summary>The acting user; defaults to the current user (set explicitly for sign-in events).</summary>
    public Guid? UserId { get; init; }

    public string? UserName { get; init; }

    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public string? BeforeJson { get; init; }

    public string? AfterJson { get; init; }
}

/// <summary>Appends audit records. Inside a transaction the record commits or rolls back with it.</summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}
