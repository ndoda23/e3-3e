namespace CreditFlow.Application.ReferenceData;

/// <summary>
/// Codes of the seeded roles (the roles of the CreditFlow task). Used ONLY for seeding reference data and in tests.
/// Authorization code must check permissions, never role codes. (These are constants, so usage cannot be detected by
/// architecture tests; the role-specific behaviour that is not a permission is data on the role instead.)
/// </summary>
public static class RoleCodes
{
    public const string Operator = "OP";
    public const string Underwriter = "UW";
    public const string SeniorUnderwriter = "SUW";
    public const string CreditCommittee = "CC";
    public const string Compliance = "COMP";
    public const string Auditor = "AUD";

    /// <summary>The Admin role. Its code predates CreditFlow and is kept (role codes are immutable).</summary>
    public const string Admin = "SYSADMIN";
}
