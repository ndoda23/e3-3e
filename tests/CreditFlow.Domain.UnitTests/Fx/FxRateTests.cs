using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;

namespace CreditFlow.Domain.UnitTests.Fx;

public sealed class FxRateTests
{
    private static readonly DateOnly Day = new(2026, 10, 12);

    [Fact]
    public void A_rate_converts_to_and_from_GEL_with_the_system_rounding()
    {
        var usd = FxRate.Create(Currency.USD, Day, 2.7134m).Value.ToExchangeRate();

        Assert.Equal(Money.Of(2713.40m, Currency.GEL), usd.ToGel(Money.Of(1000m, Currency.USD)));
        Assert.Equal(Money.Of(36.85m, Currency.USD), usd.FromGel(Money.Of(100m, Currency.GEL)));
    }

    [Fact]
    public void GEL_converts_to_itself()
    {
        Assert.Equal(Money.Of(10m, Currency.GEL), ExchangeRate.Gel(Day).ToGel(Money.Of(10m, Currency.GEL)));
    }

    [Fact]
    public void A_rate_converts_only_its_own_currency()
    {
        var usd = FxRate.Create(Currency.USD, Day, 2.7m).Value.ToExchangeRate();

        Assert.Throws<InvalidOperationException>(() => usd.ToGel(Money.Of(1m, Currency.EUR)));
        Assert.Throws<InvalidOperationException>(() => usd.FromGel(Money.Of(1m, Currency.USD)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2.1234567)]
    public void Rates_must_be_positive_with_at_most_six_decimals(double rate)
    {
        Assert.Equal(FxErrors.InvalidRate, FxRate.Create(Currency.EUR, Day, (decimal)rate).Error);
    }

    [Fact]
    public void There_is_no_rate_for_GEL()
    {
        Assert.Equal(FxErrors.InvalidCurrency, FxRate.Create(Currency.GEL, Day, 1m).Error);
    }
}
