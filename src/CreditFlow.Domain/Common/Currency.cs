namespace CreditFlow.Domain.Common;

/// <summary>Currencies the bank lends in. GEL is the base currency: limits and thresholds are expressed in GEL.</summary>
public enum Currency
{
    GEL = 1,
    USD = 2,
    EUR = 3,
}
