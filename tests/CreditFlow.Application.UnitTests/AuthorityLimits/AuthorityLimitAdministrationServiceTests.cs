using CreditFlow.Application.AuthorityLimits;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Application.Roles;
using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.UnitTests.AuthorityLimits;

public sealed class AuthorityLimitAdministrationServiceTests
{
    private readonly FakeReferenceRepositories _repositories = new FakeReferenceRepositories().WithSeededLimits();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly AuthorityLimitAdministrationService _service;

    public AuthorityLimitAdministrationServiceTests()
    {
        _service = new AuthorityLimitAdministrationService(
            _repositories,
            _repositories,
            _unitOfWork,
            new CreateCreditDecisionLimitCommandValidator(),
            new UpdateCreditDecisionLimitCommandValidator(),
            new CreatePricingExceptionLimitCommandValidator(),
            new UpdatePricingExceptionLimitCommandValidator());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Overview_separates_the_two_limit_types()
    {
        var overview = await _service.GetOverviewAsync(Ct);

        Assert.Equal(4, overview.CreditDecision.Count);
        Assert.Equal(2, overview.PricingException.Count);
        Assert.Contains(overview.CreditDecision, l => l is { RoleCode: "BM", MaxAmount: 150_000m });
    }

    [Fact]
    public async Task Credit_limit_amount_can_be_changed()
    {
        var senior = _repositories.Limits.Single(l => l.AuthorityLevel == AuthorityLevel.Senior);

        var result = await _service.UpdateCreditDecisionAsync(new UpdateCreditDecisionLimitCommand(senior.Id, 60_000m), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(60_000m, senior.MaxAmount);
    }

    [Fact]
    public async Task A_USD_limit_can_be_added_for_an_underwriter_level()
    {
        var result = await _service.CreateCreditDecisionAsync(
            new CreateCreditDecisionLimitCommand(
                TestReferenceData.RoleId(RoleCodes.Underwriter), AuthorityLevel.Senior, Currency.USD, 18_000m),
            Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Duplicate_credit_limit_is_rejected()
    {
        var result = await _service.CreateCreditDecisionAsync(
            new CreateCreditDecisionLimitCommand(
                TestReferenceData.RoleId(RoleCodes.Underwriter), AuthorityLevel.Senior, Currency.GEL, 1m),
            Ct);

        Assert.Equal(AuthorityLimitErrors.Duplicate, result.Error);
    }

    [Fact]
    public async Task Credit_limit_level_must_follow_the_role()
    {
        var bmWithLevel = await _service.CreateCreditDecisionAsync(
            new CreateCreditDecisionLimitCommand(
                TestReferenceData.RoleId(RoleCodes.BranchManager), AuthorityLevel.Senior, Currency.USD, 1m),
            Ct);
        var uwWithoutLevel = await _service.CreateCreditDecisionAsync(
            new CreateCreditDecisionLimitCommand(
                TestReferenceData.RoleId(RoleCodes.Underwriter), null, Currency.USD, 1m),
            Ct);

        Assert.Equal(AuthorityLimitAdministrationService.AuthorityLevelNotAllowed, bmWithLevel.Error);
        Assert.Equal(AuthorityLimitAdministrationService.AuthorityLevelRequired, uwWithoutLevel.Error);
    }

    [Fact]
    public async Task Limits_cannot_be_given_to_the_administrator_role()
    {
        var result = await _service.CreatePricingExceptionAsync(
            new CreatePricingExceptionLimitCommand(TestReferenceData.RoleId(RoleCodes.SystemAdministrator), 1m, 10m),
            Ct);

        Assert.Equal(AuthorityLimitAdministrationService.RoleMustBeBusinessRole, result.Error);
    }

    [Fact]
    public async Task Pricing_thresholds_can_be_changed_and_cleared()
    {
        var head = _repositories.Limits.Single(l => l.RoleId == TestReferenceData.RoleId(RoleCodes.Head));

        var result = await _service.UpdatePricingExceptionAsync(
            new UpdatePricingExceptionLimitCommand(head.Id, 2.5m, null), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2.5m, head.MaxRateGapPp);
        Assert.Null(head.MinCustomerRoaePct);
    }

    [Fact]
    public async Task Invalid_pricing_thresholds_are_rejected()
    {
        var head = _repositories.Limits.Single(l => l.RoleId == TestReferenceData.RoleId(RoleCodes.Head));

        var result = await _service.UpdatePricingExceptionAsync(
            new UpdatePricingExceptionLimitCommand(head.Id, 0m, 150m), Ct);

        Assert.Equal(
            [AuthorityLimitErrors.InvalidMaxRateGap.Code, AuthorityLimitErrors.InvalidMinCustomerRoae.Code],
            result.Error!.Details.Select(d => d.Code));
        Assert.Equal(2.0m, head.MaxRateGapPp);
    }

    [Fact]
    public async Task Updating_a_limit_of_the_other_type_is_not_found()
    {
        var head = _repositories.Limits.Single(l => l.RoleId == TestReferenceData.RoleId(RoleCodes.Head));

        var result = await _service.UpdateCreditDecisionAsync(new UpdateCreditDecisionLimitCommand(head.Id, 1m), Ct);

        Assert.Equal(AuthorityLimitErrors.NotFound, result.Error);
        Assert.Equal(RoleAdministrationErrors.NotFound.Type, result.Error!.Type);
    }
}
