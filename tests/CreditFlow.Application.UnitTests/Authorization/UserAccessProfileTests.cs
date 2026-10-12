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
        var staff = TestReferenceData.Staff(RoleCodes.Underwriter, Vake, authorityLevel: AuthorityLevel.Senior);
        var role = TestReferenceData.Profile(RoleCodes.Underwriter);

        var profile = UserAccessProfile.Create(staff, role, "Underwriter", ["applications.decide"], "Vake");

        Assert.Equal(staff.Id, profile.UserId);
        Assert.Equal(role.Id, profile.RoleId);
        Assert.Equal("UW", profile.RoleCode);
        Assert.Equal(role.DataScope, profile.DataScope);
        Assert.Equal(Vake, profile.BranchId);
        Assert.Equal("Vake", profile.BranchName);
        Assert.Equal(AuthorityLevel.Senior, profile.AuthorityLevel);
        Assert.True(profile.HasPermission("applications.decide"));
        Assert.False(profile.HasPermission("applications.create"));
    }

    [Fact]
    public void Inactive_user_holds_no_permissions()
    {
        var staff = TestReferenceData.Staff(RoleCodes.Underwriter, Vake, authorityLevel: AuthorityLevel.Senior);
        staff.Deactivate();

        var profile = TestReferenceData.AccessProfile(staff);

        Assert.Empty(profile.Permissions);
        Assert.False(profile.HasPermission(Permissions.Applications.ViewFull));
        Assert.False(profile.HasAnyAdministrativePermission);
    }

    [Fact]
    public void Profile_requires_the_staff_members_own_role()
    {
        var staff = TestReferenceData.Staff(RoleCodes.Director);

        Assert.Throws<ArgumentException>(() =>
            UserAccessProfile.Create(staff, TestReferenceData.Profile(RoleCodes.Head), "Head", []));
    }

    [Fact]
    public void Administrators_hold_administrative_permissions_and_business_users_do_not()
    {
        var admin = TestReferenceData.AccessProfile(TestReferenceData.Staff(RoleCodes.SystemAdministrator));
        var underwriter = TestReferenceData.AccessProfile(
            TestReferenceData.Staff(RoleCodes.Underwriter, Vake, authorityLevel: AuthorityLevel.Junior));

        Assert.True(admin.HasAnyAdministrativePermission);
        Assert.False(underwriter.HasAnyAdministrativePermission);
    }

    [Fact]
    public void HasAnyPermission_is_true_when_one_matches()
    {
        var aml = TestReferenceData.AccessProfile(TestReferenceData.Staff(RoleCodes.AmlAnalyst));

        Assert.True(aml.HasAnyPermission(Permissions.Applications.ViewFull, Permissions.Aml.ViewScreening));
        Assert.False(aml.HasAnyPermission(Permissions.Applications.ViewFull, Permissions.Pricing.ViewExceptions));
    }
}
