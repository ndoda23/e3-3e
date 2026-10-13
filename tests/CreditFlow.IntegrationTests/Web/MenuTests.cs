using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using CreditFlow.Web.Navigation;
using CreditFlow.Web.TagHelpers;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Sidebar and "Create Application" visibility per seeded role (permissions only, never role names).</summary>
public sealed class MenuTests
{
    public static TheoryData<string, string[], bool> Expectations => new()
    {
        { RoleCodes.Operator, ["Dashboard", "Applications", "Clients", "Loans", "Reports"], true },
        { RoleCodes.Underwriter, ["Dashboard", "Inbox", "Applications", "Clients", "Loans", "Reports"], false },
        { RoleCodes.SeniorUnderwriter, ["Dashboard", "Inbox", "Applications", "Clients", "Loans", "Reports"], false },
        { RoleCodes.CreditCommittee, ["Dashboard", "Inbox", "Applications", "Clients", "Loans", "Reports"], false },
        { RoleCodes.Compliance, ["Dashboard", "Inbox", "Applications", "Clients"], false },
        { RoleCodes.Auditor, ["Dashboard", "Applications", "Clients", "Loans", "Reports", "Audit"], false },
        { RoleCodes.Admin, ["Users", "Roles", "Branches", "Products", "ScoringRules", "ApprovalThresholds", "FxRates", "Holidays", "AuditLog"], false },
    };

    [Theory]
    [MemberData(nameof(Expectations))]
    public void Each_role_sees_only_its_permitted_menu_items(string roleCode, string[] expectedItems, bool canCreate)
    {
        var profile = ProfileFor(roleCode);

        var sections = Menu.For(profile);

        Assert.Equal(expectedItems, sections.SelectMany(s => s.Items).Select(i => i.Key));
        Assert.Equal(canCreate, Menu.CanCreateApplication(profile));
    }

    [Fact]
    public void Administration_section_appears_only_with_admin_permissions()
    {
        Assert.DoesNotContain(Menu.For(ProfileFor(RoleCodes.Underwriter)), s => s.Key == "Administration");
        Assert.DoesNotContain(Menu.For(ProfileFor(RoleCodes.Admin)), s => s.Key == "Workspace");
    }

    [Fact]
    public void An_inactive_user_sees_nothing()
    {
        var profile = ProfileFor(RoleCodes.Operator) with { IsActive = false, Permissions = new HashSet<string>() };

        Assert.Empty(Menu.For(profile));
        Assert.False(Menu.CanCreateApplication(profile));
    }

    [Fact]
    public void Removing_a_permission_hides_its_menu_item()
    {
        var seed = RoleSeedCatalog.Get(RoleCodes.Underwriter);
        var profile = ProfileFor(RoleCodes.Underwriter) with
        {
            Permissions = seed.Permissions.Where(p => p != Permissions.Reports.View).ToHashSet(),
        };

        Assert.DoesNotContain(Menu.For(profile).SelectMany(s => s.Items), i => i.Key == "Reports");
    }

    [Fact]
    public void Every_menu_icon_exists()
    {
        Assert.All(Menu.Workspace.Concat(Menu.Administration), item => Assert.True(IconTagHelper.Exists(item.Icon), item.Icon));
    }

    internal static UserAccessProfile ProfileFor(string roleCode)
    {
        var seed = RoleSeedCatalog.Get(roleCode);
        var role = new RoleProfile
        {
            Id = RoleId.New(),
            Code = RoleCode.Create(seed.Code).Value,
            Kind = seed.Kind,
            DataScope = seed.DataScope,
            Dashboard = seed.Dashboard,
            BranchRule = seed.BranchRule,
        };

        return UserAccessProfile.FromParts(
            UserId.New(), "Test User", role, seed.DisplayName, seed.Permissions,
            seed.BranchRule == AttributeRule.Required ? BranchId.New() : null, "Vake",
            isActive: true);
    }
}
