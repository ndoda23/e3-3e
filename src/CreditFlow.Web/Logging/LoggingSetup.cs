using System.Data;
using System.Globalization;
using CreditFlow.Infrastructure.Logging;
using CreditFlow.Infrastructure.Options;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

namespace CreditFlow.Web.Logging;

/// <summary>Settings of the SQL Server log sink (configuration section <c>DatabaseLog</c>).</summary>
public sealed class DatabaseLogOptions
{
    public const string SectionName = "DatabaseLog";

    /// <summary>Write logs to the <c>Logs</c> table.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The lowest level written to the database (the console follows <c>Serilog:MinimumLevel</c>).</summary>
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

    /// <summary>How often buffered events are written.</summary>
    public TimeSpan BatchPeriod { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// The logging pipeline. Levels, overrides and enrichers come from configuration (<c>Serilog</c> section); the sinks
/// are configured here because every sink is wrapped in <see cref="MaskingSink"/>, which configuration cannot express.
/// </summary>
public static class LoggingSetup
{
    public static LoggerConfiguration WriteToMaskedSinks(
        this LoggerConfiguration logger, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(DatabaseLogOptions.SectionName).Get<DatabaseLogOptions>() ?? new();
        var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);

        var masked = LoggerSinkConfiguration.Wrap(
            sink => new MaskingSink(sink),
            sinks =>
            {
                sinks.Console(formatProvider: CultureInfo.InvariantCulture);
                if (options.Enabled && !string.IsNullOrWhiteSpace(connectionString))
                {
                    sinks.MSSqlServer(
                        connectionString,
                        new MSSqlServerSinkOptions
                        {
                            TableName = LogEntry.TableName,
                            AutoCreateSqlTable = false, // created by the EF Core migration AddLogs
                            BatchPeriod = options.BatchPeriod,
                        },
                        restrictedToMinimumLevel: options.MinimumLevel,
                        formatProvider: CultureInfo.InvariantCulture,
                        columnOptions: ColumnOptions());
                }
            });
        return logger.WriteTo.Sink(masked);
    }

    /// <summary>The columns of <see cref="LogEntry"/>.</summary>
    private static ColumnOptions ColumnOptions()
    {
        var columns = new ColumnOptions();
        columns.Store.Remove(StandardColumn.MessageTemplate);
        columns.Store.Remove(StandardColumn.Properties);
        columns.Store.Add(StandardColumn.LogEvent);

        columns.Id.DataType = SqlDbType.BigInt;
        columns.TimeStamp.DataType = SqlDbType.DateTimeOffset;
        columns.TimeStamp.ConvertToUtc = true;
        columns.Level.DataLength = LogEntry.LevelMaxLength;

        // Every property not stored in its own column, as JSON.
        columns.LogEvent.ColumnName = nameof(LogEntry.Properties);
        columns.LogEvent.ExcludeStandardColumns = true;
        columns.LogEvent.ExcludeAdditionalProperties = true;

        columns.AdditionalColumns =
        [
            new SqlColumn(LogContextMiddleware.UserName, SqlDbType.NVarChar, dataLength: LogEntry.UserNameMaxLength),
            new SqlColumn(LogContextMiddleware.RequestPath, SqlDbType.NVarChar, dataLength: LogEntry.RequestPathMaxLength),
            new SqlColumn(LogContextMiddleware.CorrelationId, SqlDbType.NVarChar, dataLength: LogEntry.CorrelationIdMaxLength),
        ];
        return columns;
    }
}
