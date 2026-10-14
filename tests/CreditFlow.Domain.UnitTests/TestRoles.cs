using CreditFlow.Domain.Roles;

namespace CreditFlow.Domain.UnitTests;

/// <summary>Role profiles mirroring the seeded roles, built locally so Domain tests do not depend on Application.</summary>
internal static class TestRoles
{
    public static readonly RoleProfile Operator = Profile(
        "OP", RoleKind.Business, DataScope.Branch, DashboardKind.Operator, AttributeRule.Required);

    public static readonly RoleProfile Underwriter = Profile(
        "UW", RoleKind.Business, DataScope.Organization, DashboardKind.Underwriting, AttributeRule.NotAllowed);

    public static readonly RoleProfile Auditor = Profile(
        "AUD", RoleKind.ReadOnly, DataScope.Organization, DashboardKind.Audit, AttributeRule.NotAllowed);

    public static readonly RoleProfile Admin = Profile(
        "SYSADMIN", RoleKind.Administrative, DataScope.None, DashboardKind.Administration, AttributeRule.NotAllowed);

    /// <summary>A role whose branch may be set but is not required (no seeded role uses this rule).</summary>
    public static readonly RoleProfile OptionalBranch = Profile(
        "OPT", RoleKind.Business, DataScope.Organization, DashboardKind.Underwriting, AttributeRule.Optional);

    private static RoleProfile Profile(
        string code, RoleKind kind, DataScope scope, DashboardKind dashboard, AttributeRule branch) =>
        new()
        {
            Id = RoleId.New(),
            Code = RoleCode.Create(code).Value,
            Kind = kind,
            DataScope = scope,
            Dashboard = dashboard,
            BranchRule = branch,
        };
}
