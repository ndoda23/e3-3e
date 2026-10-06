using Lams.Application.Authorization;
using Lams.Application.ReferenceData;
using Lams.Domain.AuthorityLimits;
using Lams.Domain.Branches;
using Lams.Domain.Users;

namespace Lams.Application.UnitTests.Authorization;

/// <summary>Both authority limit types, evaluated with the seeded limits and fake resources.</summary>
public sealed class AuthorityLimitEvaluatorTests
{
    private static readonly BranchId Vake = BranchId.New();
    private readonly AuthorityLimitEvaluator _evaluator = new(new FakeReferenceRepositories().WithSeededLimits());

    private sealed record FakeLoan(decimal Amount, Currency Currency = Currency.GEL) : ICreditDecisionResource;

    private sealed record FakeException(Segment Segment, decimal RateGapPp, decimal CustomerRoaePct) : IPricedResource;

    [Theory]
    [InlineData(AuthorityLevel.Junior, 15_000, CreditDecisionOutcome.Within)]
    [InlineData(AuthorityLevel.Junior, 15_001, CreditDecisionOutcome.Escalate)]
    [InlineData(AuthorityLevel.Standard, 30_000, CreditDecisionOutcome.Within)]
    [InlineData(AuthorityLevel.Standard, 30_001, CreditDecisionOutcome.Escalate)]
    [InlineData(AuthorityLevel.Senior, 50_000, CreditDecisionOutcome.Within)]
    [InlineData(AuthorityLevel.Senior, 50_001, CreditDecisionOutcome.Escalate)]
    public async Task Underwriter_decides_within_the_level_limit(
        AuthorityLevel level, int amount, CreditDecisionOutcome expected)
    {
        var underwriter = Profile(TestReferenceData.Staff(RoleCodes.Underwriter, Vake, authorityLevel: level));

        var evaluation = await _evaluator.EvaluateCreditDecisionAsync(
            underwriter, new FakeLoan(amount), TestContext.Current.CancellationToken);

        Assert.Equal(expected, evaluation.Outcome);
    }

    [Theory]
    [InlineData(150_000, CreditDecisionOutcome.Within)]
    [InlineData(150_001, CreditDecisionOutcome.Escalate)]
    public async Task Branch_manager_decides_within_the_branch_limit(int amount, CreditDecisionOutcome expected)
    {
        var manager = Profile(TestReferenceData.Staff(RoleCodes.BranchManager, Vake));

        var evaluation = await _evaluator.EvaluateCreditDecisionAsync(
            manager, new FakeLoan(amount), TestContext.Current.CancellationToken);

        Assert.Equal(expected, evaluation.Outcome);
    }

    [Fact]
    public async Task Currency_without_a_limit_escalates()
    {
        var underwriter = Profile(TestReferenceData.Staff(RoleCodes.Underwriter, Vake, authorityLevel: AuthorityLevel.Senior));

        var evaluation = await _evaluator.EvaluateCreditDecisionAsync(
            underwriter, new FakeLoan(1_000, Currency.USD), TestContext.Current.CancellationToken);

        Assert.Equal(CreditDecisionOutcome.Escalate, evaluation.Outcome);
    }

    [Theory]
    [InlineData(RoleCodes.Head)]
    [InlineData(RoleCodes.Director)]
    [InlineData(RoleCodes.FinancialAnalysis)]
    public async Task Roles_without_the_decide_permission_are_not_authorized_to_decide(string roleCode)
    {
        var user = Profile(TestReferenceData.Staff(roleCode, segment: Segment.Retail));

        var evaluation = await _evaluator.EvaluateCreditDecisionAsync(
            user, new FakeLoan(1_000), TestContext.Current.CancellationToken);

        Assert.Equal(CreditDecisionOutcome.NotAuthorized, evaluation.Outcome);
    }

    [Fact]
    public async Task Loan_officer_is_not_authorized_to_decide()
    {
        var officer = Profile(TestReferenceData.Staff(RoleCodes.LoanOfficer, Vake, authorityLevel: AuthorityLevel.Senior));

        var evaluation = await _evaluator.EvaluateCreditDecisionAsync(
            officer, new FakeLoan(1_000), TestContext.Current.CancellationToken);

        Assert.Equal(CreditDecisionOutcome.NotAuthorized, evaluation.Outcome);
    }

    [Fact]
    public async Task Inactive_underwriter_is_not_authorized()
    {
        var staff = TestReferenceData.Staff(RoleCodes.Underwriter, Vake, authorityLevel: AuthorityLevel.Senior);
        staff.Deactivate();

        var evaluation = await _evaluator.EvaluateCreditDecisionAsync(
            Profile(staff), new FakeLoan(1_000), TestContext.Current.CancellationToken);

        Assert.Equal(CreditDecisionOutcome.NotAuthorized, evaluation.Outcome);
    }

    [Theory]
    [InlineData(1.99, 15.0, PricingExceptionOutcome.Within)]
    [InlineData(2.0, 15.0, PricingExceptionOutcome.RouteToDirector)]
    [InlineData(1.0, 14.99, PricingExceptionOutcome.RouteToDirector)]
    public async Task Head_of_retail_approves_retail_exceptions_within_limit(
        double gap, double roae, PricingExceptionOutcome expected)
    {
        var head = Profile(TestReferenceData.Staff(RoleCodes.Head, segment: Segment.Retail));

        var outcome = await _evaluator.EvaluatePricingExceptionAsync(
            head, new FakeException(Segment.Retail, (decimal)gap, (decimal)roae), TestContext.Current.CancellationToken);

        Assert.Equal(expected, outcome);
    }

    [Fact]
    public async Task Head_of_retail_cannot_approve_business_exceptions()
    {
        var head = Profile(TestReferenceData.Staff(RoleCodes.Head, segment: Segment.Retail));

        var outcome = await _evaluator.EvaluatePricingExceptionAsync(
            head, new FakeException(Segment.Business, 0.5m, 30m), TestContext.Current.CancellationToken);

        Assert.Equal(PricingExceptionOutcome.NotAuthorized, outcome);
    }

    [Fact]
    public async Task Head_without_a_segment_handles_nothing()
    {
        var head = Profile(TestReferenceData.Staff(RoleCodes.Head, segment: Segment.Retail)) with { Segment = null };

        var outcome = await _evaluator.EvaluatePricingExceptionAsync(
            head, new FakeException(Segment.Retail, 0.5m, 30m), TestContext.Current.CancellationToken);

        Assert.Equal(PricingExceptionOutcome.NotAuthorized, outcome);
    }

    [Fact]
    public async Task Director_approves_any_exception_of_any_segment()
    {
        var director = Profile(TestReferenceData.Staff(RoleCodes.Director));

        var outcome = await _evaluator.EvaluatePricingExceptionAsync(
            director, new FakeException(Segment.Business, 9m, 2m), TestContext.Current.CancellationToken);

        Assert.Equal(PricingExceptionOutcome.Within, outcome);
    }

    [Fact]
    public async Task FAB_cannot_approve_exceptions()
    {
        var fab = Profile(TestReferenceData.Staff(RoleCodes.FinancialAnalysis));

        var outcome = await _evaluator.EvaluatePricingExceptionAsync(
            fab, new FakeException(Segment.Retail, 0.5m, 30m), TestContext.Current.CancellationToken);

        Assert.Equal(PricingExceptionOutcome.NotAuthorized, outcome);
    }

    // ----- Own limits (dashboard) -----

    [Theory]
    [InlineData(AuthorityLevel.Junior, 15_000)]
    [InlineData(AuthorityLevel.Standard, 30_000)]
    [InlineData(AuthorityLevel.Senior, 50_000)]
    public async Task Underwriter_sees_the_limit_of_their_level(AuthorityLevel level, int expected)
    {
        var underwriter = Profile(TestReferenceData.Staff(RoleCodes.Underwriter, Vake, authorityLevel: level));

        var limits = await _evaluator.ListCreditDecisionLimitsAsync(underwriter, TestContext.Current.CancellationToken);

        Assert.Equal([new CreditDecisionLimit(Currency.GEL, expected)], limits);
    }

    [Fact]
    public async Task Branch_manager_sees_the_branch_limit()
    {
        var manager = Profile(TestReferenceData.Staff(RoleCodes.BranchManager, Vake));

        var limits = await _evaluator.ListCreditDecisionLimitsAsync(manager, TestContext.Current.CancellationToken);

        Assert.Equal([new CreditDecisionLimit(Currency.GEL, 150_000)], limits);
    }

    [Theory]
    [InlineData(RoleCodes.LoanOfficer)]
    [InlineData(RoleCodes.Director)]
    public async Task Users_who_cannot_decide_have_no_credit_decision_limit(string roleCode)
    {
        var user = Profile(TestReferenceData.Staff(roleCode, Vake, authorityLevel: AuthorityLevel.Senior));

        var limits = await _evaluator.ListCreditDecisionLimitsAsync(user, TestContext.Current.CancellationToken);

        Assert.Empty(limits);
    }

    private static UserAccessProfile Profile(StaffMember staff) => TestReferenceData.AccessProfile(staff);
}
