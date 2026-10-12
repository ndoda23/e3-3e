namespace CreditFlow.Domain.Users;

/// <summary>
/// Seniority of a loan officer or underwriter. For underwriters it selects the credit decision limit;
/// for loan officers it is informational only.
/// </summary>
public enum AuthorityLevel
{
    Junior = 1,
    Standard = 2,
    Senior = 3,
}
