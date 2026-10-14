using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.UnitTests.Common;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("1.005", "1.01")]
    [InlineData("1.004", "1.00")]
    [InlineData("-1.005", "-1.01")]
    [InlineData("2.675", "2.68")]
    [InlineData("0.125", "0.13")]
    public void Rounding_is_two_decimals_half_away_from_zero(string value, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            Money.Round(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Amounts_are_rounded_when_created()
    {
        Assert.Equal(10.01m, Money.Of(10.005m, Currency.GEL).Amount);
    }

    [Fact]
    public void Arithmetic_keeps_the_currency_and_rounds()
    {
        var a = Money.Of(100m, Currency.USD);
        var b = Money.Of(0.10m, Currency.USD);

        Assert.Equal(Money.Of(100.10m, Currency.USD), a + b);
        Assert.Equal(Money.Of(99.90m, Currency.USD), a - b);
        Assert.Equal(Money.Of(33.33m, Currency.USD), a.Multiply(1m / 3m));
        Assert.Equal(Money.Of(1.50m, Currency.USD), a.Percent(1.5m));
        Assert.True(a > b);
    }

    [Fact]
    public void Different_currencies_cannot_be_combined_or_compared()
    {
        var gel = Money.Of(1m, Currency.GEL);
        var usd = Money.Of(1m, Currency.USD);

        Assert.Throws<InvalidOperationException>(() => gel + usd);
        Assert.Throws<InvalidOperationException>(() => gel < usd);
    }

    [Fact]
    public void Equality_is_by_amount_and_currency()
    {
        Assert.Equal(Money.Of(5m, Currency.EUR), Money.Of(5.000m, Currency.EUR));
        Assert.NotEqual(Money.Of(5m, Currency.EUR), Money.Of(5m, Currency.USD));
        Assert.Equal("5.00 EUR", Money.Of(5m, Currency.EUR).ToString());
    }
}
