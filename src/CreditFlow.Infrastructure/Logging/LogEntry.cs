namespace CreditFlow.Infrastructure.Logging;

/// <summary>
/// A row of the <c>Logs</c> table, written by the Serilog SQL Server sink (masked before it is written). The table is
/// created by an EF Core migration, not by the sink; this entity is used to read logs back.
/// </summary>
public sealed class LogEntry
{
    public const string TableName = "Logs";
    public const int LevelMaxLength = 16;
    public const int UserNameMaxLength = 256;
    public const int RequestPathMaxLength = 2048;
    public const int CorrelationIdMaxLength = 64;

    public long Id { get; set; }

    public DateTimeOffset TimeStamp { get; set; }

    public string Level { get; set; } = string.Empty;

    public string? Message { get; set; }

    public string? Exception { get; set; }

    /// <summary>The signed-in user's name, when the event happened inside a request of a signed-in user.</summary>
    public string? UserName { get; set; }

    public string? RequestPath { get; set; }

    /// <summary>The request's trace identifier (also stored on audit records).</summary>
    public string? CorrelationId { get; set; }

    /// <summary>All other event properties as JSON.</summary>
    public string? Properties { get; set; }
}
