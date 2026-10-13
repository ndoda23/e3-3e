using CreditFlow.Application.Administration;
using CreditFlow.Domain.Common;

namespace CreditFlow.Application.UnitTests.Administration;

/// <summary>The FX import format: header and blank lines ignored, comma or semicolon, every error names its line.</summary>
public sealed class FxRateCsvTests
{
    [Fact]
    public void A_file_with_a_header_and_both_separators_is_read()
    {
        var rows = FxRateCsv.Parse("﻿Currency,EffectiveDate,GelPerUnit\r\nUSD,2026-10-13,2.7150\r\n\r\neur; 2026-10-13 ; 3.1700\n").Value;

        Assert.Equal(
            [new FxRateRow(2, Currency.USD, new DateOnly(2026, 10, 13), 2.7150m), new FxRateRow(4, Currency.EUR, new DateOnly(2026, 10, 13), 3.17m)],
            rows);
    }

    [Theory]
    [InlineData("GEL,2026-10-13,1", "Line 1: unknown currency")]
    [InlineData("CHF,2026-10-13,3", "Line 1: unknown currency")]
    [InlineData("USD,13.10.2026,2.7", "Line 1: the date")]
    [InlineData("USD,2026-10-13,2,7", "Line 1: expected")]
    [InlineData("USD,2026-10-13,-2.7", "Line 1: the rate")]
    [InlineData("USD,2026-10-13,abc", "Line 1: the rate")]
    public void A_wrong_line_is_reported_with_its_number(string line, string message)
    {
        var parsed = FxRateCsv.Parse(line);

        Assert.True(parsed.IsFailure);
        Assert.StartsWith(message, parsed.Error!.Description, StringComparison.Ordinal); // a single error is not wrapped
    }

    [Fact]
    public void Every_wrong_line_is_reported_and_none_is_accepted()
    {
        var parsed = FxRateCsv.Parse("USD,2026-10-13,2.71\nUSD,2026-10-13,2.72\nXXX,2026-10-13,1\n");

        Assert.True(parsed.IsFailure);
        Assert.Equal(["Line 2", "Line 3"], parsed.Error!.Details.Select(e => e.Description[..6]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Currency,EffectiveDate,GelPerUnit\n\n")]
    public void An_empty_file_is_refused(string text)
    {
        Assert.Equal(FxRateCsv.Empty, FxRateCsv.Parse(text).Error);
    }

    [Fact]
    public void Too_many_lines_are_refused()
    {
        var start = new DateOnly(2000, 1, 1);
        var text = string.Join('\n', Enumerable.Range(0, FxRateCsv.MaxLines + 1)
            .Select(i => $"USD,{start.AddDays(i):yyyy-MM-dd},2.7"));

        Assert.Equal(FxRateCsv.TooLong, FxRateCsv.Parse(text).Error);
    }
}
