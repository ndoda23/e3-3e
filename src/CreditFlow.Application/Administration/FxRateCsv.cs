using System.Globalization;
using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Administration;

/// <summary>One parsed line of an FX rate file.</summary>
public sealed record FxRateRow(int Line, Currency Currency, DateOnly EffectiveDate, decimal GelPerUnit);

/// <summary>
/// The FX rate import format: one rate per line, <c>Currency,EffectiveDate,GelPerUnit</c> (also <c>;</c>-separated),
/// e.g. <c>USD,2026-10-13,2.7150</c>. An optional header line, blank lines and surrounding spaces are ignored; the date
/// is <c>yyyy-MM-dd</c> and the rate uses a dot. Every error names its line, and the whole file is rejected if any
/// line is wrong (at most <see cref="MaxLines"/> lines, so an accidental huge file cannot block the database).
/// </summary>
public static class FxRateCsv
{
    public const int MaxLines = 5_000;

    public static readonly Error Empty = Error.Validation("FxImport.Empty", "The file contains no rates.", "File");

    public static readonly Error TooLong = Error.Validation(
        "FxImport.TooLong", $"A file may contain at most {MaxLines} rates.", "File");

    public static Result<IReadOnlyList<FxRateRow>> Parse(string? text)
    {
        var lines = (text ?? string.Empty).Split('\n');
        var rows = new List<FxRateRow>();
        var errors = new List<Error>();
        var seen = new HashSet<(Currency, DateOnly)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var number = i + 1;
            var line = lines[i].Trim().TrimStart('﻿');
            if (line.Length == 0 || (rows.Count == 0 && errors.Count == 0 && line.StartsWith("Currency", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var parts = line.Split(line.Contains(';', StringComparison.Ordinal) ? ';' : ',').Select(p => p.Trim().Trim('"')).ToArray();
            if (parts.Length != 3)
            {
                errors.Add(LineError(number, "expected Currency,EffectiveDate,GelPerUnit."));
                continue;
            }

            if (!Enum.TryParse<Currency>(parts[0], ignoreCase: true, out var currency) || !Enum.IsDefined(currency) || currency == Currency.GEL)
            {
                errors.Add(LineError(number, $"unknown currency '{parts[0]}' (USD or EUR)."));
                continue;
            }

            if (!DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                errors.Add(LineError(number, $"the date '{parts[1]}' is not yyyy-MM-dd."));
                continue;
            }

            if (!decimal.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) || rate <= 0m)
            {
                errors.Add(LineError(number, $"the rate '{parts[2]}' is not a positive number."));
                continue;
            }

            if (!seen.Add((currency, date)))
            {
                errors.Add(LineError(number, $"{currency} on {parts[1]} appears twice."));
                continue;
            }

            rows.Add(new FxRateRow(number, currency, date, rate));
        }

        if (errors.Count > 0)
        {
            return Result.Failure<IReadOnlyList<FxRateRow>>(Error.ValidationFailed(errors));
        }

        return rows.Count switch
        {
            0 => Result.Failure<IReadOnlyList<FxRateRow>>(Empty),
            > MaxLines => Result.Failure<IReadOnlyList<FxRateRow>>(TooLong),
            _ => Result.Success<IReadOnlyList<FxRateRow>>(rows),
        };
    }

    public static Error LineError(int line, string message) =>
        Error.Validation("FxImport.InvalidLine", $"Line {line}: {message}", "File");
}
