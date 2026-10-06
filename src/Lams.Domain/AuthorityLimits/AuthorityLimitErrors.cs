using Lams.Domain.Common;

namespace Lams.Domain.AuthorityLimits;

public static class AuthorityLimitErrors
{
    public static readonly Error InvalidMaxAmount = Error.Validation(
        "AuthorityLimit.InvalidMaxAmount", "The maximum amount must be greater than zero.", "MaxAmount");

    public static readonly Error InvalidMaxRateGap = Error.Validation(
        "AuthorityLimit.InvalidMaxRateGap",
        "The maximum rate gap must be greater than 0 and at most 100 percentage points.",
        "MaxRateGapPp");

    public static readonly Error InvalidMinCustomerRoae = Error.Validation(
        "AuthorityLimit.InvalidMinCustomerRoae",
        "The minimum customer ROAE must be between 0 and 100 percent.",
        "MinCustomerRoaePct");

    public static readonly Error InvalidCurrency = Error.Validation(
        "AuthorityLimit.InvalidCurrency", "The currency is not valid.", "Currency");

    public static readonly Error InvalidAuthorityLevel = Error.Validation(
        "AuthorityLimit.InvalidAuthorityLevel", "The authority level is not valid.", "AuthorityLevel");

    public static readonly Error Duplicate = Error.Conflict(
        "AuthorityLimit.Duplicate", "A limit already exists for this role, level and currency.");

    public static readonly Error NotFound = Error.NotFound(
        "AuthorityLimit.NotFound", "The authority limit was not found.");
}
