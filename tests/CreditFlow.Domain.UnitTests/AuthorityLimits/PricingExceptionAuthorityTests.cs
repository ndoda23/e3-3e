using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.UnitTests.AuthorityLimits;

/// <summary>Pricing exception limits as seeded: HD gap &lt; 2.0 pp and ROAE ≥ 15 %, own segment only; DIR unlimited.</summary>
public sealed class PricingExceptionAuthorityTests
{
    private static readonly RoleId Head = RoleId.New();
    private static readonly RoleId Director = RoleId.New();

    private static readonly AuthorityLimit[] Limits =
    [
        AuthorityLimit.CreatePricingException(Head, maxRateGapPp: 2.0m, minCustomerRoaePct: 15m).Value,
        AuthorityLimit.CreatePricingException(Director, null, null).Value,
    ];

    [Theory]
    [InlineData(1.99, 15.0, PricingExceptionOutcome.Within)]
    [InlineData(0.0, 40.0, PricingExceptionOutcome.Within)]
    [InlineData(2.0, 15.0, PricingExceptionOutcome.RouteToDirector)]
    [InlineData(1.0, 14.99, PricingExceptionOutcome.RouteToDirector)]
    [InlineData(2.5, 10.0, PricingExceptionOutcome.RouteToDirector)]
    public void Head_approves_only_within_both_thresholds(double gap, double roae, PricingExceptionOutcome expected)
    {
        var outcome = EvaluateHead(Segment.Retail, Segment.Retail, (decimal)gap, (decimal)roae);

        Assert.Equal(expected, outcome);
    }

    [Fact]
    public void Head_cannot_handle_another_segment()
    {
        var outcome = EvaluateHead(headSegment: Segment.Retail, exceptionSegment: Segment.Business, 0.5m, 30m);

        Assert.Equal(PricingExceptionOutcome.NotAuthorized, outcome);
    }

    [Fact]
    public void Head_of_business_handles_business_exceptions()
    {
        Assert.Equal(PricingExceptionOutcome.Within, EvaluateHead(Segment.Business, Segment.Business, 1m, 20m));
    }

    [Fact]
    public void Director_has_no_limit()
    {
        var outcome = PricingExceptionAuthority.Evaluate(
            canApprove: true,
            Director,
            restrictedToSegment: null,
            Limits,
            new PricingExceptionRequest(Segment.Business, RateGapPp: 12m, CustomerRoaePct: 1m));

        Assert.Equal(PricingExceptionOutcome.Within, outcome);
    }

    [Fact]
    public void A_user_without_the_approve_permission_is_not_authorized()
    {
        var outcome = PricingExceptionAuthority.Evaluate(
            false, Director, null, Limits, new PricingExceptionRequest(Segment.Retail, 0.1m, 50m));

        Assert.Equal(PricingExceptionOutcome.NotAuthorized, outcome);
    }

    [Fact]
    public void No_limit_row_routes_to_the_director()
    {
        var outcome = PricingExceptionAuthority.Evaluate(
            true, RoleId.New(), null, Limits, new PricingExceptionRequest(Segment.Retail, 0.1m, 50m));

        Assert.Equal(PricingExceptionOutcome.RouteToDirector, outcome);
    }

    [Fact]
    public void Negative_rate_gap_is_a_programming_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PricingExceptionAuthority.Evaluate(
            true, Head, Segment.Retail, Limits, new PricingExceptionRequest(Segment.Retail, -0.1m, 20m)));
    }

    private static PricingExceptionOutcome EvaluateHead(
        Segment headSegment, Segment exceptionSegment, decimal gap, decimal roae) =>
        PricingExceptionAuthority.Evaluate(
            canApprove: true,
            Head,
            restrictedToSegment: headSegment,
            Limits,
            new PricingExceptionRequest(exceptionSegment, gap, roae));
}
