using CreditFlow.Infrastructure.Logging;
using CreditFlow.IntegrationTests.Infrastructure;
using CreditFlow.Web.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Logs go to the <c>Logs</c> table with user and request path, and sensitive data never reaches a sink.</summary>
public sealed partial class LoggingTests(SqlServerContainer sql) : WebTestBase(sql)
{
    private const string PersonalNumber = "01001011001";

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["Serilog:MinimumLevel:Default"] = "Information",
        ["DatabaseLog:BatchPeriod"] = "00:00:00.200",
    };

    [Fact]
    public async Task A_log_event_reaches_the_database_with_personal_numbers_and_passwords_masked()
    {
        var logger = Factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CreditFlow.Tests");
        var marker = Guid.NewGuid().ToString("N");

        LogClientFailed(
            logger,
            new InvalidOperationException($"Duplicate key value is ({PersonalNumber})."),
            marker,
            PersonalNumber,
            "Very!Secret-123");

        var entry = await WaitForLogAsync(l => l.Message!.Contains(marker));

        Assert.Equal("Warning", entry.Level);
        Assert.Contains("*******1001", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PersonalNumber, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Very!Secret-123", entry.Message, StringComparison.Ordinal);
        Assert.Contains("*******1001", entry.Exception, StringComparison.Ordinal);
        Assert.DoesNotContain(PersonalNumber, entry.Exception, StringComparison.Ordinal);
        Assert.DoesNotContain(PersonalNumber, entry.Properties ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Very!Secret-123", entry.Properties ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_logs_carry_the_signed_in_user_and_the_request_path()
    {
        var (userName, _, password) = await CreateOperatorAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        await browser.GetPageAsync("/Account/Manage");

        var entry = await WaitForLogAsync(l => l.RequestPath == "/Account/Manage" && l.UserName == userName);
        Assert.Equal("Information", entry.Level);
        Assert.False(string.IsNullOrEmpty(entry.CorrelationId));
    }

    [Fact]
    public void The_masking_sink_masks_template_text_nested_values_and_exceptions()
    {
        var collected = new List<LogEvent>();
        var sink = new MaskingSink(new CollectingSink(collected));
        var nested = new StructureValue(
        [
            new LogEventProperty("Name", new ScalarValue("Anna")),
            new LogEventProperty("PersonalNumber", new ScalarValue(PersonalNumber)),
            new LogEventProperty("Notes", new SequenceValue([new ScalarValue($"see {PersonalNumber}")])),
        ]);

        sink.Emit(new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Warning,
            new InvalidOperationException($"bad {PersonalNumber}"),
            new MessageTemplateParser().Parse($"Interpolated {PersonalNumber} and {{Client}} with {{Token}}"),
            [
                new LogEventProperty("Client", nested),
                new LogEventProperty("Token", new ScalarValue("abc.def.ghi")),
                new LogEventProperty("Number", new ScalarValue(35001022334L)),
            ]));

        var masked = Assert.Single(collected);
        var rendered = masked.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + masked.Exception + string.Join(";", masked.Properties.Values);
        Assert.DoesNotContain(PersonalNumber, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def.ghi", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("35001022334", rendered, StringComparison.Ordinal);
        Assert.Contains("Anna", rendered, StringComparison.Ordinal);
        Assert.Contains("*******1001", masked.MessageTemplate.Text, StringComparison.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Marker {Marker}: client {PersonalNumber} failed, password {Password}")]
    private static partial void LogClientFailed(
        ILogger logger, Exception exception, string marker, string personalNumber, string password);

    private async Task<LogEntry> WaitForLogAsync(System.Linq.Expressions.Expression<Func<LogEntry, bool>> predicate)
    {
        // The sink writes in batches; poll until the event arrives (fails after 15 seconds).
        for (var attempt = 0; attempt < 75; attempt++)
        {
            var entry = await QueryAsync(db => db.Logs.AsNoTracking().Where(predicate).FirstOrDefaultAsync(Ct));
            if (entry is not null)
            {
                return entry;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), Ct);
        }

        var recent = await QueryAsync(db => db.Logs.AsNoTracking().OrderByDescending(l => l.Id).Take(10)
            .Select(l => l.Level + " " + l.UserName + " " + l.RequestPath + " " + l.Message).ToListAsync(Ct));
        throw new TimeoutException("The log event did not reach the Logs table. Latest: " + string.Join(" | ", recent));
    }

    private sealed class CollectingSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
