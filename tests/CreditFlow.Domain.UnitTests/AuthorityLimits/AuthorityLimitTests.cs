using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.UnitTests.AuthorityLimits;

public sealed class AuthorityLimitTests
{
    private static readonly RoleId Underwriter = RoleId.New();

    [Fact]
    public void Credit_decision_limit_requires_a_positive_amount()
    {
        Assert.Equal(
            AuthorityLimitErrors.InvalidMaxAmount,
            AuthorityLimit.CreateCreditDecision(Underwriter, AuthorityLevel.Junior, Currency.GEL, 0m).Error);
    }

    [Fact]
    public void Credit_decision_limit_rejects_undefined_currency_or_level()
    {
        Assert.Equal(
            AuthorityLimitErrors.InvalidCurrency,
            AuthorityLimit.CreateCreditDecision(Underwriter, null, (Currency)99, 1m).Error);
        Assert.Equal(
            AuthorityLimitErrors.InvalidAuthorityLevel,
            AuthorityLimit.CreateCreditDecision(Underwriter, (AuthorityLevel)99, Currency.GEL, 1m).Error);
    }

    [Fact]
    public void Credit_decision_limit_amount_can_be_changed()
    {
        var limit = AuthorityLimit.CreateCreditDecision(Underwriter, AuthorityLevel.Senior, Currency.GEL, 50_000m).Value;

        Assert.True(limit.ChangeCreditDecision(60_000m).IsSuccess);
        Assert.Equal(60_000m, limit.MaxAmount);
        Assert.True(limit.ChangeCreditDecision(-1m).IsFailure);
        Assert.Equal(60_000m, limit.MaxAmount);
    }

    [Theory]
    [InlineData(0.0, null)]
    [InlineData(-1.0, null)]
    [InlineData(100.5, null)]
    public void Pricing_limit_rejects_an_invalid_rate_gap(double gap, double? roae)
    {
        var result = AuthorityLimit.CreatePricingException(Underwriter, (decimal)gap, (decimal?)roae);

        Assert.Equal(AuthorityLimitErrors.InvalidMaxRateGap, result.Error);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    public void Pricing_limit_rejects_an_invalid_minimum_roae(double roae)
    {
        var result = AuthorityLimit.CreatePricingException(Underwriter, 2m, (decimal)roae);

        Assert.Equal(AuthorityLimitErrors.InvalidMinCustomerRoae, result.Error);
    }

    [Fact]
    public void Pricing_limit_without_thresholds_is_unlimited()
    {
        var limit = AuthorityLimit.CreatePricingException(Underwriter, null, null).Value;

        Assert.True(limit.AllowsPricingException(rateGapPp: 25m, customerRoaePct: -10m));
    }

    [Fact]
    public void Limit_types_are_not_interchangeable()
    {
        var credit = AuthorityLimit.CreateCreditDecision(Underwriter, null, Currency.GEL, 1m).Value;
        var pricing = AuthorityLimit.CreatePricingException(Underwriter, 2m, 15m).Value;

        Assert.Throws<InvalidOperationException>(() => credit.AllowsPricingException(1m, 20m));
        Assert.Throws<InvalidOperationException>(() => pricing.ChangeCreditDecision(1m));
    }
}
