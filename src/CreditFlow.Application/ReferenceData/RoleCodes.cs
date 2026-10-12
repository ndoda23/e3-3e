namespace CreditFlow.Application.ReferenceData;

/// <summary>
/// Codes of the seeded roles. Used ONLY for seeding reference data and in tests.
/// Authorization code must check permissions, never role codes. (These are constants, so usage cannot be detected by
/// architecture tests; the role-specific behaviour that is not a permission is data on the role instead.)
/// </summary>
public static class RoleCodes
{
    public const string LoanOfficer = "LO";
    public const string Underwriter = "UW";
    public const string BranchManager = "BM";
    public const string BusinessDevelopmentManager = "BDM";
    public const string BackOffice = "BO";
    public const string AmlAnalyst = "AML";
    public const string FinancialAnalysis = "FAB";
    public const string Head = "HD";
    public const string Director = "DIR";
    public const string SystemAdministrator = "SYSADMIN";
}
