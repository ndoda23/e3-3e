using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.ReferenceData;

public sealed record AuthorityLimitSeed(
    string RoleCode,
    LimitType LimitType,
    AuthorityLevel? AuthorityLevel = null,
    Currency? Currency = null,
    decimal? MaxAmount = null,
    decimal? MaxRateGapPp = null,
    decimal? MinCustomerRoaePct = null);

/// <summary>Initial authority limits (seeded once; administrators edit them afterwards).</summary>
public static class AuthorityLimitSeedCatalog
{
    public static IReadOnlyList<AuthorityLimitSeed> All { get; } =
    [
        // Credit decision (FRD Flow C): above the limit the user can only escalate.
        new(RoleCodes.Underwriter, LimitType.CreditDecision, AuthorityLevel.Junior, Currency.GEL, MaxAmount: 15_000m),
        new(RoleCodes.Underwriter, LimitType.CreditDecision, AuthorityLevel.Standard, Currency.GEL, MaxAmount: 30_000m),
        new(RoleCodes.Underwriter, LimitType.CreditDecision, AuthorityLevel.Senior, Currency.GEL, MaxAmount: 50_000m),
        new(RoleCodes.BranchManager, LimitType.CreditDecision, null, Currency.GEL, MaxAmount: 150_000m),

        // Pricing exception (FRD Flow B): Head approves if gap < 2.0 pp AND ROAE >= 15 %, otherwise the Director.
        new(RoleCodes.Head, LimitType.PricingException, MaxRateGapPp: 2.0m, MinCustomerRoaePct: 15m),
        new(RoleCodes.Director, LimitType.PricingException),
    ];
}
