using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Roles;

namespace CreditFlow.Application.UnitTests.ReferenceData;

public sealed class ReferenceDataTests
{
    [Fact]
    public void The_roles_are_exactly_the_seven_roles_of_the_task()
    {
        Assert.Equal(
            ["AUD", "CC", "COMP", "OP", "SUW", "SYSADMIN", "UW"],
            RoleSeedCatalog.All.Select(r => r.Code).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Admin", "Auditor", "Compliance", "Credit Committee", "Operator", "Senior Underwriter", "Underwriter"],
            RoleSeedCatalog.All.Select(r => r.DisplayName).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("OP", RoleKind.Business, DataScope.Branch, DashboardKind.Operator, AttributeRule.Required)]
    [InlineData("UW", RoleKind.Business, DataScope.Organization, DashboardKind.Underwriting, AttributeRule.NotAllowed)]
    [InlineData("SUW", RoleKind.Business, DataScope.Organization, DashboardKind.Underwriting, AttributeRule.NotAllowed)]
    [InlineData("CC", RoleKind.Business, DataScope.Organization, DashboardKind.Committee, AttributeRule.NotAllowed)]
    [InlineData("COMP", RoleKind.Business, DataScope.Organization, DashboardKind.Compliance, AttributeRule.NotAllowed)]
    [InlineData("AUD", RoleKind.ReadOnly, DataScope.Organization, DashboardKind.Audit, AttributeRule.NotAllowed)]
    [InlineData("SYSADMIN", RoleKind.Administrative, DataScope.None, DashboardKind.Administration, AttributeRule.NotAllowed)]
    public void Roles_have_the_expected_definition(
        string code, RoleKind kind, DataScope scope, DashboardKind dashboard, AttributeRule branch)
    {
        var role = RoleSeedCatalog.Get(code);

        Assert.Equal(kind, role.Kind);
        Assert.Equal(scope, role.DataScope);
        Assert.Equal(dashboard, role.Dashboard);
        Assert.Equal(branch, role.BranchRule);
    }

    [Fact]
    public void Every_seeded_role_satisfies_segregation_of_duties()
    {
        Assert.All(RoleSeedCatalog.All, role =>
            Assert.True(PermissionCatalog.CreateSet(role.Kind, role.Permissions).IsSuccess, role.Code));
    }

    [Fact]
    public void Admin_holds_every_admin_permission()
    {
        var admin = RoleSeedCatalog.Get(RoleCodes.Admin);

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
}
