using CreditFlow.Application.Common;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace CreditFlow.Web.Logging;

/// <summary>
/// Wraps every log sink (console and SQL Server) and masks sensitive data before anything is written, using the same
/// rule as the audit log (<see cref="SensitiveData"/>): personal numbers anywhere (message template text, property
/// values at any depth, exception text) keep only their last four digits, and properties named like a password,
/// token or document number are masked by name.
/// </summary>
internal sealed class MaskingSink(ILogEventSink inner) : ILogEventSink, IDisposable, IAsyncDisposable
{
    private static readonly MessageTemplateParser TemplateParser = new();

    public void Emit(LogEvent logEvent) => inner.Emit(Mask(logEvent));

    public void Dispose() => (inner as IDisposable)?.Dispose();

    public async ValueTask DisposeAsync()
    {
        switch (inner)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    /// <summary>A copy of the event with every sensitive value masked.</summary>
    internal static LogEvent Mask(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var templateText = SensitiveData.MaskText(logEvent.MessageTemplate.Text);
        var template = templateText == logEvent.MessageTemplate.Text
            ? logEvent.MessageTemplate
            : TemplateParser.Parse(templateText);

        return new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.Exception is null ? null : new MaskedException(logEvent.Exception),
            template,
            logEvent.Properties.Select(p => new LogEventProperty(p.Key, MaskValue(p.Key, p.Value))),
            logEvent.TraceId ?? default,
            logEvent.SpanId ?? default);
    }

    private static LogEventPropertyValue MaskValue(string name, LogEventPropertyValue value)
    {
        if (SensitiveData.IsSensitiveField(name))
        {
            return new ScalarValue(SensitiveData.MaskField(name, value is ScalarValue scalar ? scalar.Value?.ToString() : value.ToString()));
        }

        return value switch
        {
            ScalarValue { Value: string text } => new ScalarValue(SensitiveData.MaskText(text)),
            ScalarValue { Value: long or ulong or decimal } scalar => MaskNumber(scalar),
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(e => MaskValue(string.Empty, e))),
            StructureValue structure => new StructureValue(
                structure.Properties.Select(p => new LogEventProperty(p.Name, MaskValue(p.Name, p.Value))),
                structure.TypeTag),
            DictionaryValue dictionary => new DictionaryValue(dictionary.Elements.Select(e =>
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    e.Key, MaskValue(e.Key.Value?.ToString() ?? string.Empty, e.Value)))),
            _ => value,
        };
    }

    /// <summary>A personal number logged as a number (e.g. <c>long</c>) is masked like text.</summary>
    private static ScalarValue MaskNumber(ScalarValue scalar)
    {
        var text = Convert.ToString(scalar.Value, System.Globalization.CultureInfo.InvariantCulture)!;
        var masked = SensitiveData.MaskText(text);
        return masked == text ? scalar : new ScalarValue(masked);
    }

    /// <summary>
    /// Stands in for the original exception so that sinks render masked text (message and stack trace), including
    /// database errors that quote a duplicate personal number.
    /// </summary>
    private sealed class MaskedException(Exception original) : Exception(SensitiveData.MaskText(original.Message))
    {
        private readonly string _text = SensitiveData.MaskText(original.ToString());

        public override string ToString() => _text;
    }
}
