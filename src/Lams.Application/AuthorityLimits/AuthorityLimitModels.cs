using FluentValidation;
using Lams.Domain.AuthorityLimits;
using Lams.Domain.Roles;
using Lams.Domain.Users;

namespace Lams.Application.AuthorityLimits;

/// <summary>A limit row with its role's code and name, for the admin screen.</summary>
public sealed record AuthorityLimitItem(
    AuthorityLimitId Id,
    RoleId RoleId,
    string RoleCode,
    string RoleName,
    AuthorityLevel? AuthorityLevel,
    LimitType LimitType,
    Currency? Currency,
    decimal? MaxAmount,
    decimal? MaxRateGapPp,
    decimal? MinCustomerRoaePct);

public sealed record AuthorityLimitOverview(
    IReadOnlyList<AuthorityLimitItem> CreditDecision,
    IReadOnlyList<AuthorityLimitItem> PricingException);

public sealed record CreateCreditDecisionLimitCommand(
    RoleId RoleId, AuthorityLevel? AuthorityLevel, Currency Currency, decimal MaxAmount);

public sealed record UpdateCreditDecisionLimitCommand(AuthorityLimitId Id, decimal MaxAmount);

public sealed record CreatePricingExceptionLimitCommand(RoleId RoleId, decimal? MaxRateGapPp, decimal? MinCustomerRoaePct);

public sealed record UpdatePricingExceptionLimitCommand(
    AuthorityLimitId Id, decimal? MaxRateGapPp, decimal? MinCustomerRoaePct);

public sealed class CreateCreditDecisionLimitCommandValidator : AbstractValidator<CreateCreditDecisionLimitCommand>
{
    public CreateCreditDecisionLimitCommandValidator()
    {
        RuleFor(c => c.AuthorityLevel).IsInEnum()
            .WithErrorCode(AuthorityLimitErrors.InvalidAuthorityLevel.Code)
            .WithMessage(AuthorityLimitErrors.InvalidAuthorityLevel.Description);
        RuleFor(c => c.Currency).IsInEnum()
            .WithErrorCode(AuthorityLimitErrors.InvalidCurrency.Code)
            .WithMessage(AuthorityLimitErrors.InvalidCurrency.Description);
        RuleFor(c => c.MaxAmount).GreaterThan(0)
            .WithErrorCode(AuthorityLimitErrors.InvalidMaxAmount.Code)
            .WithMessage(AuthorityLimitErrors.InvalidMaxAmount.Description);
    }
}

public sealed class UpdateCreditDecisionLimitCommandValidator : AbstractValidator<UpdateCreditDecisionLimitCommand>
{
    public UpdateCreditDecisionLimitCommandValidator()
    {
        RuleFor(c => c.MaxAmount).GreaterThan(0)
            .WithErrorCode(AuthorityLimitErrors.InvalidMaxAmount.Code)
            .WithMessage(AuthorityLimitErrors.InvalidMaxAmount.Description);
    }
}

public sealed class CreatePricingExceptionLimitCommandValidator
    : AbstractValidator<CreatePricingExceptionLimitCommand>
{
    public CreatePricingExceptionLimitCommandValidator()
    {
        RuleFor(c => c.MaxRateGapPp).PricingRateGap();
        RuleFor(c => c.MinCustomerRoaePct).PricingMinRoae();
    }
}

public sealed class UpdatePricingExceptionLimitCommandValidator
    : AbstractValidator<UpdatePricingExceptionLimitCommand>
{
    public UpdatePricingExceptionLimitCommandValidator()
    {
        RuleFor(c => c.MaxRateGapPp).PricingRateGap();
        RuleFor(c => c.MinCustomerRoaePct).PricingMinRoae();
    }
}

internal static class PricingLimitRules
{
    public static IRuleBuilderOptions<T, decimal?> PricingRateGap<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.Must(gap => gap is null or > 0 and <= AuthorityLimit.MaxPercentage)
            .WithErrorCode(AuthorityLimitErrors.InvalidMaxRateGap.Code)
            .WithMessage(AuthorityLimitErrors.InvalidMaxRateGap.Description);

    public static IRuleBuilderOptions<T, decimal?> PricingMinRoae<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.Must(roae => roae is null or >= 0 and <= AuthorityLimit.MaxPercentage)
            .WithErrorCode(AuthorityLimitErrors.InvalidMinCustomerRoae.Code)
            .WithMessage(AuthorityLimitErrors.InvalidMinCustomerRoae.Description);
}
