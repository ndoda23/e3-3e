using System.Globalization;
using CreditFlow.Domain.Common;
using CreditFlow.Web.Infrastructure;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Times are shown in Tbilisi (UTC+4) whatever the server's zone; amounts with thousand separators.</summary>
public sealed class DisplayFormatTests
{
    [Fact]
    public void Times_are_shown_in_Tbilisi_time()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var instant = new DateTimeOffset(2026, 10, 13, 8, 56, 0, TimeSpan.Zero);

            Assert.Equal("10/13/2026 12:56 PM", DisplayFormat.DateTime(instant));
            Assert.Equal(DisplayFormat.Empty, DisplayFormat.DateTime(null));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Amounts_have_thousand_separators()
    {
        Assert.Equal("150,000.00", DisplayFormat.Amount(150_000m));
        Assert.Equal("1,234,567.50", DisplayFormat.Amount(1_234_567.5m));
        Assert.Equal("₾150,000", DisplayFormat.Money(Currency.GEL, 150_000m));
    }
}
