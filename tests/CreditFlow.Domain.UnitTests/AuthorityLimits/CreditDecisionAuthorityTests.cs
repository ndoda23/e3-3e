using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.UnitTests.AuthorityLimits;

/// <summary>Credit decision limits as seeded: UW Junior 15k / Standard 30k / Senior 50k GEL, BM 150k GEL.</summary>
public sealed class CreditDecisionAuthorityTests
{
    private static readonly RoleId Underwriter = RoleId.New();
    private static readonly RoleId BranchManager = RoleId.New();

    private static readonly AuthorityLimit[] Limits =
    [
        AuthorityLimit.CreateCreditDecision(Underwriter, AuthorityLevel.Junior, Currency.GEL, 15_000m).Value,
        AuthorityLimit.CreateCreditDecision(Underwriter, AuthorityLevel.Standard, Currency.GEL, 30_000m).Value,
        AuthorityLimit.CreateCreditDecision(Underwriter, AuthorityLevel.Senior, Currency.GEL, 50_000m).Value,
        AuthorityLimit.CreateCreditDecision(BranchManager, null, Currency.GEL, 150_000m).Value,
    ];

    [Theory]
    [InlineData(AuthorityLevel.Junior, 15_000, CreditDecisionOutcome.Within)]
    [InlineData(AuthorityLevel.Junior, 15_000.01, CreditDecisionOutcome.Escalate)]
    [InlineData(AuthorityLevel.Standard, 30_000, CreditDecisionOutcome.Within)]
    [InlineData(AuthorityLevel.Standard, 30_001, CreditDecisionOutcome.Escalate)]
    [InlineData(AuthorityLevel.Senior, 50_000, CreditDecisionOutcome.Within)]
    [InlineData(AuthorityLevel.Senior, 50_001, CreditDecisionOutcome.Escalate)]
    public void Underwriter_limit_rises_with_level(AuthorityLevel level, double amount, CreditDecisionOutcome expected)
    {
        var evaluation = CreditDecisionAuthority.Evaluate(
            canDecide: true, Underwriter, level, Limits, Currency.GEL, (decimal)amount);

        Assert.Equal(expected, evaluation.Outcome);
    }

    [Theory]
    [InlineData(150_000, CreditDecisionOutcome.Within)]
    [InlineData(150_001, CreditDecisionOutcome.Escalate)]
    public void Branch_manager_has_a_branch_limit(double amount, CreditDecisionOutcome expected)
    {
        var evaluation = CreditDecisionAuthority.Evaluate(
            true, BranchManager, null, Limits, Currency.GEL, (decimal)amount);

        Assert.Equal(expected, evaluation.Outcome);
        Assert.Equal(150_000m, evaluation.Limit);
    }

    [Fact]
    public void No_limit_row_for_the_currency_escalates_so_the_file_is_never_stuck()
    {
        var evaluation = CreditDecisionAuthority.Evaluate(
            true, Underwriter, AuthorityLevel.Senior, Limits, Currency.USD, 1_000m);

        Assert.Equal(CreditDecisionOutcome.Escalate, evaluation.Outcome);
        Assert.Null(evaluation.Limit);
    }

    [Fact]
    public void No_limit_row_for_the_level_escalates()
    {
        var evaluation = CreditDecisionAuthority.Evaluate(true, Underwriter, null, Limits, Currency.GEL, 1_000m);

        Assert.Equal(CreditDecisionOutcome.Escalate, evaluation.Outcome);
    }

    [Fact]
    public void A_user_without_the_decide_permission_is_not_authorized()
    {
        var evaluation = CreditDecisionAuthority.Evaluate(
            canDecide: false, Underwriter, AuthorityLevel.Senior, Limits, Currency.GEL, 1_000m);

        Assert.Equal(CreditDecisionOutcome.NotAuthorized, evaluation.Outcome);
    }

    [Fact]
    public void Limits_of_other_roles_are_ignored()
    {
        // A Branch Manager limit row must never apply to an underwriter.
        var evaluation = CreditDecisionAuthority.Evaluate(
            true, Underwriter, null, [Limits[3]], Currency.GEL, 100_000m);

        Assert.Equal(CreditDecisionOutcome.Escalate, evaluation.Outcome);
    }

    [Fact]
    public void Amount_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreditDecisionAuthority.Evaluate(
            true, Underwriter, AuthorityLevel.Senior, Limits, Currency.GEL, 0m));
    }
}
