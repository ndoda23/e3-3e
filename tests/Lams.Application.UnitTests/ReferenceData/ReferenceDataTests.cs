using Lams.Application.Authorization;
using Lams.Application.ReferenceData;
using Lams.Domain.AuthorityLimits;
using Lams.Domain.Roles;
using Lams.Domain.Users;

namespace Lams.Application.UnitTests.ReferenceData;

public sealed class ReferenceDataTests
{
    [Fact]
    public void There_are_exactly_nine_business_roles_and_one_technical_role()
    {
        Assert.Equal(10, RoleSeedCatalog.All.Count);
        Assert.Single(RoleSeedCatalog.All, r => r.IsAdministrative);
        Assert.Equal(
            ["AML", "BDM", "BM", "BO", "DIR", "FAB", "HD", "LO", "SYSADMIN", "UW"],
            RoleSeedCatalog.All.Select(r => r.Code).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("LO", DataScope.Own, DashboardKind.Officer)]
    [InlineData("UW", DataScope.Assigned, DashboardKind.Underwriter)]
    [InlineData("BM", DataScope.Branch, DashboardKind.Branch)]
    [InlineData("BDM", DataScope.Organization, DashboardKind.BusinessDevelopment)]
    [InlineData("BO", DataScope.Organization, DashboardKind.BackOffice)]
    [InlineData("AML", DataScope.Organization, DashboardKind.AmlScreening)]
    [InlineData("FAB", DataScope.Organization, DashboardKind.Fab)]
    [InlineData("HD", DataScope.Segment, DashboardKind.Exceptions)]
    [InlineData("DIR", DataScope.Organization, DashboardKind.Exceptions)]
    [InlineData("SYSADMIN", DataScope.None, DashboardKind.Administration)]
    public void Roles_have_the_expected_scope_and_dashboard(string code, DataScope scope, DashboardKind dashboard)
    {
        var role = RoleSeedCatalog.Get(code);

        Assert.Equal(scope, role.DataScope);
        Assert.Equal(dashboard, role.Dashboard);
    }

    [Theory]
    [InlineData("LO", AttributeRule.Required, AttributeRule.NotAllowed, AttributeRule.Required)]
    [InlineData("UW", AttributeRule.Required, AttributeRule.NotAllowed, AttributeRule.Required)]
    [InlineData("BM", AttributeRule.Required, AttributeRule.NotAllowed, AttributeRule.NotAllowed)]
    [InlineData("HD", AttributeRule.Optional, AttributeRule.Required, AttributeRule.NotAllowed)]
    [InlineData("SYSADMIN", AttributeRule.NotAllowed, AttributeRule.NotAllowed, AttributeRule.NotAllowed)]
    public void Roles_govern_the_user_attributes(
        string code, AttributeRule branch, AttributeRule segment, AttributeRule level)
    {
        var role = RoleSeedCatalog.Get(code);

        Assert.Equal(branch, role.BranchRule);
        Assert.Equal(segment, role.SegmentRule);
        Assert.Equal(level, role.AuthorityLevelRule);
    }

    [Fact]
    public void Every_seeded_role_satisfies_segregation_of_duties()
    {
        Assert.All(RoleSeedCatalog.All, role =>
            Assert.True(PermissionSet.Create(role.IsAdministrative, role.Permissions).IsSuccess, role.Code));
    }

    [Fact]
    public void System_administrator_holds_all_nine_admin_permissions()
    {
        var admin = RoleSeedCatalog.Get(RoleCodes.SystemAdministrator);

        Assert.Equal(9, admin.Permissions.Count);
        Assert.All(admin.Permissions, p => Assert.StartsWith("admin.", p, StringComparison.Ordinal));
        Assert.Equal(PermissionCatalog.All.Count(p => p.IsAdministrative), admin.Permissions.Count);
    }

    [Fact]
    public void The_six_branches_are_seeded()
    {
        Assert.Equal(
            ["Batumi", "Kutaisi", "Rustavi", "Saburtalo", "Tbilisi HQ", "Vake"],
            BranchSeedCatalog.All.Select(b => b.Name).Order(StringComparer.Ordinal));
        Assert.Equal(6, BranchSeedCatalog.All.Select(b => b.Code).Distinct().Count());
    }

    [Fact]
    public void Credit_decision_limits_match_the_brief()
    {
        var credit = AuthorityLimitSeedCatalog.All.Where(l => l.LimitType == LimitType.CreditDecision).ToList();

        Assert.Equal(4, credit.Count);
        Assert.All(credit, l => Assert.Equal(Currency.GEL, l.Currency));
        Assert.Contains(credit, l => l is { RoleCode: "UW", AuthorityLevel: AuthorityLevel.Junior, MaxAmount: 15_000m });
        Assert.Contains(credit, l => l is { RoleCode: "UW", AuthorityLevel: AuthorityLevel.Standard, MaxAmount: 30_000m });
        Assert.Contains(credit, l => l is { RoleCode: "UW", AuthorityLevel: AuthorityLevel.Senior, MaxAmount: 50_000m });
        Assert.Contains(credit, l => l is { RoleCode: "BM", AuthorityLevel: null, MaxAmount: 150_000m });
    }

    [Fact]
    public void Pricing_exception_limits_match_the_brief()
    {
        var pricing = AuthorityLimitSeedCatalog.All.Where(l => l.LimitType == LimitType.PricingException).ToList();

        Assert.Equal(2, pricing.Count);
        Assert.Contains(pricing, l => l is { RoleCode: "HD", MaxRateGapPp: 2.0m, MinCustomerRoaePct: 15m });
        Assert.Contains(pricing, l => l is { RoleCode: "DIR", MaxRateGapPp: null, MinCustomerRoaePct: null });
    }
}
