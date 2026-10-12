using CreditFlow.Domain.Roles;

namespace CreditFlow.Domain.UnitTests;

/// <summary>Role profiles mirroring the seeded roles, built locally so Domain tests do not depend on Application.</summary>
internal static class TestRoles
{
    public static readonly RoleProfile LoanOfficer = Profile("LO", DataScope.Own, DashboardKind.Officer,
        branch: AttributeRule.Required, segment: AttributeRule.NotAllowed, level: AttributeRule.Required);

    public static readonly RoleProfile Underwriter = Profile("UW", DataScope.Assigned, DashboardKind.Underwriter,
        branch: AttributeRule.Required, segment: AttributeRule.NotAllowed, level: AttributeRule.Required);

    public static readonly RoleProfile BranchManager = Profile("BM", DataScope.Branch, DashboardKind.Branch,
        branch: AttributeRule.Required, segment: AttributeRule.NotAllowed, level: AttributeRule.NotAllowed);

    public static readonly RoleProfile BackOffice = Profile("BO", DataScope.Organization, DashboardKind.BackOffice,
        branch: AttributeRule.Optional, segment: AttributeRule.NotAllowed, level: AttributeRule.NotAllowed);

    public static readonly RoleProfile Head = Profile("HD", DataScope.Segment, DashboardKind.Exceptions,
        branch: AttributeRule.Optional, segment: AttributeRule.Required, level: AttributeRule.NotAllowed);

    public static readonly RoleProfile Director = Profile("DIR", DataScope.Organization, DashboardKind.Exceptions,
        branch: AttributeRule.Optional, segment: AttributeRule.NotAllowed, level: AttributeRule.NotAllowed);

    public static readonly RoleProfile SystemAdministrator = Profile("SYSADMIN", DataScope.None,
        DashboardKind.Administration, branch: AttributeRule.NotAllowed, segment: AttributeRule.NotAllowed,
        level: AttributeRule.NotAllowed, isAdministrative: true);

    private static RoleProfile Profile(
        string code,
        DataScope scope,
        DashboardKind dashboard,
        AttributeRule branch,
        AttributeRule segment,
        AttributeRule level,
        bool isAdministrative = false) =>
        new()
        {
            Id = RoleId.New(),
            Code = RoleCode.Create(code).Value,
            DataScope = scope,
            Dashboard = dashboard,
            BranchRule = branch,
            SegmentRule = segment,
            AuthorityLevelRule = level,
            IsAdministrative = isAdministrative,
        };
}
