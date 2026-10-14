using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.UnitTests.Authorization;

public sealed class UserAccessProfileTests
{
    private static readonly BranchId Vake = BranchId.New();

    [Fact]
    public void Profile_is_built_from_the_staff_member_and_the_role()
    {
        var staff = TestReferenceData.Staff(RoleCodes.Operator, Vake);
        var role = TestReferenceData.Profile(RoleCodes.Operator);

        var profile = UserAccessProfile.Create(staff, role, "Operator", ["applications.create"], "Vake");

        Assert.Equal(staff.Id, profile.UserId);
        Assert.Equal(role.Id, profile.RoleId);
        Assert.Equal("OP", profile.RoleCode);
        Assert.Equal(role.DataScope, profile.DataScope);
        Assert.Equal(Vake, profile.BranchId);
        Assert.Equal("Vake", profile.BranchName);
        Assert.True(profile.HasPermission("applications.create"));
        Assert.False(profile.HasPermission("underwriting.decide"));
    }

    [Fact]
    public void Inactive_user_holds_no_permissions()
    {
        var staff = TestReferenceData.Staff(RoleCodes.Operator, Vake);
        staff.Deactivate();

        var profile = TestReferenceData.AccessProfile(staff);

        Assert.Empty(profile.Permissions);
        Assert.False(profile.HasPermission(Permissions.Applications.View));
        Assert.False(profile.HasAnyAdministrativePermission);
    }

    [Fact]
    public void Profile_requires_the_staff_members_own_role()
    {
        var staff = TestReferenceData.Staff(RoleCodes.Auditor);

        Assert.Throws<ArgumentException>(() =>
            UserAccessProfile.Create(staff, TestReferenceData.Profile(RoleCodes.Compliance), "Compliance", []));
    }

    [Fact]
    public void Administrators_hold_administrative_permissions_and_business_users_do_not()
    {
        var admin = TestReferenceData.AccessProfile(TestReferenceData.Staff(RoleCodes.Admin));
        var underwriter = TestReferenceData.AccessProfile(TestReferenceData.Staff(RoleCodes.Underwriter));
        var auditor = TestReferenceData.AccessProfile(TestReferenceData.Staff(RoleCodes.Auditor));

        Assert.True(admin.HasAnyAdministrativePermission);
        Assert.False(underwriter.HasAnyAdministrativePermission);
        Assert.False(auditor.HasAnyAdministrativePermission);
    }

    [Fact]
    public void HasAnyPermission_is_true_when_one_matches()
    {
        var compliance = TestReferenceData.AccessProfile(TestReferenceData.Staff(RoleCodes.Compliance));

        Assert.True(compliance.HasAnyPermission(Permissions.Underwriting.Decide, Permissions.Compliance.Review));
        Assert.False(compliance.HasAnyPermission(Permissions.Underwriting.Decide, Permissions.Committee.Vote));
    }
}
