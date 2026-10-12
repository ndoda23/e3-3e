using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.AuthorityLimits;

/// <summary>
/// An authority limit, stored as data and editable by administrators.
/// <list type="bullet">
/// <item><see cref="LimitType.CreditDecision"/>: per role, authority level and currency, the maximum loan amount.</item>
/// <item><see cref="LimitType.PricingException"/>: per role, the maximum rate gap below the policy floor (exclusive)
/// and the minimum customer ROAE (inclusive). A <c>null</c> threshold means no restriction.</item>
/// </list>
/// </summary>
public sealed class AuthorityLimit
{
    public const decimal MaxPercentage = 100m;

    private AuthorityLimit()
    {
    }

    public AuthorityLimitId Id { get; private set; }

    public RoleId RoleId { get; private set; }

    public AuthorityLevel? AuthorityLevel { get; private set; }

    public LimitType LimitType { get; private set; }

    public Currency? Currency { get; private set; }

    /// <summary>Credit decision: highest amount the user may approve (inclusive).</summary>
    public decimal? MaxAmount { get; private set; }

    /// <summary>Pricing exception: the rate gap below the policy floor must be strictly lower than this, in percentage points.</summary>
    public decimal? MaxRateGapPp { get; private set; }

    /// <summary>Pricing exception: the customer ROAE must be at least this, in percent.</summary>
    public decimal? MinCustomerRoaePct { get; private set; }

    public static Result<AuthorityLimit> CreateCreditDecision(
        RoleId roleId, AuthorityLevel? authorityLevel, Currency currency, decimal maxAmount)
    {
        if (authorityLevel is { } level && !Enum.IsDefined(level))
        {
            return Result.Failure<AuthorityLimit>(AuthorityLimitErrors.InvalidAuthorityLevel);
        }

        if (!Enum.IsDefined(currency))
        {
            return Result.Failure<AuthorityLimit>(AuthorityLimitErrors.InvalidCurrency);
        }

        var limit = new AuthorityLimit
        {
            Id = AuthorityLimitId.New(),
            RoleId = roleId,
            AuthorityLevel = authorityLevel,
            LimitType = LimitType.CreditDecision,
            Currency = currency,
        };

        var amount = limit.ChangeCreditDecision(maxAmount);
        return amount.IsSuccess ? Result.Success(limit) : Result.Failure<AuthorityLimit>(amount.Error!);
    }

    public static Result<AuthorityLimit> CreatePricingException(
        RoleId roleId, decimal? maxRateGapPp, decimal? minCustomerRoaePct)
    {
        var limit = new AuthorityLimit
        {
            Id = AuthorityLimitId.New(),
            RoleId = roleId,
            LimitType = LimitType.PricingException,
        };

        var thresholds = limit.ChangePricingException(maxRateGapPp, minCustomerRoaePct);
        return thresholds.IsSuccess ? Result.Success(limit) : Result.Failure<AuthorityLimit>(thresholds.Error!);
    }

    public Result ChangeCreditDecision(decimal maxAmount)
    {
        EnsureType(LimitType.CreditDecision);

        if (maxAmount <= 0)
        {
            return Result.Failure(AuthorityLimitErrors.InvalidMaxAmount);
        }

        MaxAmount = maxAmount;
        return Result.Success();
    }

    public Result ChangePricingException(decimal? maxRateGapPp, decimal? minCustomerRoaePct)
    {
        EnsureType(LimitType.PricingException);

        if (maxRateGapPp is { } gap && gap is <= 0 or > MaxPercentage)
        {
            return Result.Failure(AuthorityLimitErrors.InvalidMaxRateGap);
        }

        if (minCustomerRoaePct is { } roae && roae is < 0 or > MaxPercentage)
        {
            return Result.Failure(AuthorityLimitErrors.InvalidMinCustomerRoae);
        }

        MaxRateGapPp = maxRateGapPp;
        MinCustomerRoaePct = minCustomerRoaePct;
        return Result.Success();
    }

    /// <summary>True when this credit decision limit applies to the given role, level and currency (exact match).</summary>
    public bool AppliesToCreditDecision(RoleId roleId, AuthorityLevel? authorityLevel, Currency currency) =>
        LimitType == LimitType.CreditDecision
        && RoleId == roleId
        && AuthorityLevel == authorityLevel
        && Currency == currency;

    /// <summary>True when this pricing exception limit applies to the given role.</summary>
    public bool AppliesToPricingException(RoleId roleId) =>
        LimitType == LimitType.PricingException && RoleId == roleId;

    /// <summary>Whether an exception with this rate gap and customer ROAE is within this limit.</summary>
    public bool AllowsPricingException(decimal rateGapPp, decimal customerRoaePct)
    {
        EnsureType(LimitType.PricingException);

        var gapAllowed = MaxRateGapPp is not { } maxGap || rateGapPp < maxGap;
        var roaeAllowed = MinCustomerRoaePct is not { } minRoae || customerRoaePct >= minRoae;
        return gapAllowed && roaeAllowed;
    }

    private void EnsureType(LimitType expected)
    {
        if (LimitType != expected)
        {
            throw new InvalidOperationException($"Authority limit {Id} is a {LimitType} limit, not {expected}.");
        }
    }
}
