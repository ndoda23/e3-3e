using Lams.Domain.Users;

namespace Lams.Domain.UnitTests.Users;

/// <summary>At least one active administrator; no self-deactivation or self-demotion.</summary>
public sealed class AdminContinuityPolicyTests
{
    private static readonly UserId Admin = UserId.New();
    private static readonly UserId OtherAdmin = UserId.New();
    private static readonly UserId BusinessUser = UserId.New();

    [Fact]
    public void Administrator_cannot_deactivate_themselves()
    {
        var result = AdminContinuityPolicy.EnsureCanDeactivate(Admin, Admin, true, activeAdministratorCount: 3);

        Assert.Equal(UserErrors.CannotDeactivateSelf, result.Error);
    }

    [Fact]
    public void The_last_active_administrator_cannot_be_deactivated()
    {
        var result = AdminContinuityPolicy.EnsureCanDeactivate(Admin, OtherAdmin, true, activeAdministratorCount: 1);

        Assert.Equal(UserErrors.LastActiveAdministrator, result.Error);
    }

    [Fact]
    public void Another_administrator_can_be_deactivated_when_one_remains()
    {
        Assert.True(AdminContinuityPolicy.EnsureCanDeactivate(Admin, OtherAdmin, true, 2).IsSuccess);
    }

    [Fact]
    public void A_business_user_can_always_be_deactivated()
    {
        Assert.True(AdminContinuityPolicy.EnsureCanDeactivate(Admin, BusinessUser, false, 1).IsSuccess);
    }

    [Fact]
    public void Administrator_cannot_demote_themselves()
    {
        var result = AdminContinuityPolicy.EnsureCanChangeRole(
            Admin, Admin, targetIsActiveAdministrator: true, newRoleIsAdministrative: false, activeAdministratorCount: 3);

        Assert.Equal(UserErrors.CannotDemoteSelf, result.Error);
    }

    [Fact]
    public void The_last_active_administrator_cannot_be_demoted()
    {
        var result = AdminContinuityPolicy.EnsureCanChangeRole(Admin, OtherAdmin, true, false, 1);

        Assert.Equal(UserErrors.LastActiveAdministrator, result.Error);
    }

    [Fact]
    public void Another_administrator_can_be_demoted_when_one_remains()
    {
        Assert.True(AdminContinuityPolicy.EnsureCanChangeRole(Admin, OtherAdmin, true, false, 2).IsSuccess);
    }

    [Fact]
    public void Keeping_an_administrative_role_is_not_a_demotion()
    {
        Assert.True(AdminContinuityPolicy.EnsureCanChangeRole(Admin, Admin, true, true, 1).IsSuccess);
    }

    [Fact]
    public void Changing_a_business_user_role_is_allowed()
    {
        Assert.True(AdminContinuityPolicy.EnsureCanChangeRole(Admin, BusinessUser, false, false, 1).IsSuccess);
    }
}
