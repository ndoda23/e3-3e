namespace CreditFlow.Domain.Auditing;

/// <summary>Input for a new audit record.</summary>
public sealed record AuditEntry
{
    public required DateTimeOffset OccurredAt { get; init; }

    public required string Action { get; init; }

    public Guid? UserId { get; init; }

    public string? UserName { get; init; }

    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public string? BeforeJson { get; init; }

    public string? AfterJson { get; init; }

    public string? IpAddress { get; init; }

    public string? UserAgent { get; init; }

    public string? CorrelationId { get; init; }
}

/// <summary>
/// An append-only audit record. It is created once and has no way to be changed;
/// persistence additionally rejects updates and deletes.
/// </summary>
public sealed class AuditLog
{
    public const int ActionMaxLength = 100;
    public const int UserNameMaxLength = 256;
    public const int EntityTypeMaxLength = 100;
    public const int EntityIdMaxLength = 100;
    public const int IpAddressMaxLength = 45;
    public const int UserAgentMaxLength = 512;
    public const int CorrelationIdMaxLength = 100;

    private AuditLog()
    {
    }

    public AuditLogId Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public Guid? UserId { get; private set; }

    public string? UserName { get; private set; }

    public string? EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public string? BeforeJson { get; private set; }

    public string? AfterJson { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? CorrelationId { get; private set; }

    public static AuditLog Create(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Action);

        // Free-text context is truncated rather than rejected: an audit record must never fail to be written.
        return new AuditLog
        {
            OccurredAt = entry.OccurredAt,
            Action = Truncate(entry.Action.Trim(), ActionMaxLength)!,
            UserId = entry.UserId,
            UserName = Truncate(entry.UserName, UserNameMaxLength),
            EntityType = Truncate(entry.EntityType, EntityTypeMaxLength),
            EntityId = Truncate(entry.EntityId, EntityIdMaxLength),
            BeforeJson = entry.BeforeJson,
            AfterJson = entry.AfterJson,
            IpAddress = Truncate(entry.IpAddress, IpAddressMaxLength),
            UserAgent = Truncate(entry.UserAgent, UserAgentMaxLength),
            CorrelationId = Truncate(entry.CorrelationId, CorrelationIdMaxLength),
        };
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
