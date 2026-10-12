using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Branches;

namespace CreditFlow.Application.UnitTests.Applications;

/// <summary>Who sees which applications, and what a decider may decide (the SQL lists apply the same filter).</summary>
public sealed class ApplicationAccessTests
{
    private static readonly BranchId Vake = BranchId.New();
    private static readonly BranchId Batumi = BranchId.New();

    [Fact]
    public void An_operator_sees_the_whole_branch_and_nothing_else()
    {
        var filter = ApplicationAccessFilter.For(Profile(RoleCodes.Operator, Vake));

        Assert.False(filter.SeesNothing);
        Assert.Equal(Vake, filter.Branch);
    }

    [Theory]
    [InlineData(RoleCodes.Underwriter)]
    [InlineData(RoleCodes.SeniorUnderwriter)]
    [InlineData(RoleCodes.CreditCommittee)]
    [InlineData(RoleCodes.Compliance)]
    [InlineData(RoleCodes.Auditor)]
    public void Organization_roles_see_every_branch(string roleCode)
    {
        var filter = ApplicationAccessFilter.For(Profile(roleCode));

        Assert.False(filter.SeesNothing);
        Assert.Null(filter.Branch);
    }

    [Fact]
    public void Admin_sees_no_applications()
    {
        Assert.True(ApplicationAccessFilter.For(Profile(RoleCodes.Admin)).SeesNothing);
    }

    [Fact]
    public void Without_the_view_permission_nothing_is_visible()
    {
        var profile = Profile(RoleCodes.Underwriter) with { Permissions = new HashSet<string>() };

        Assert.True(ApplicationAccessFilter.For(profile).SeesNothing);
    }

    [Fact]
    public void A_branch_scope_without_a_branch_fails_closed()
    {
        var profile = Profile(RoleCodes.Operator, Batumi) with { BranchId = null };

        Assert.True(ApplicationAccessFilter.For(profile).SeesNothing);
    }

    [Theory]
    [InlineData(RoleCodes.Underwriter, ApprovalTier.Underwriter, false)]
    [InlineData(RoleCodes.SeniorUnderwriter, ApprovalTier.SeniorUnderwriter, true)]
    public void Decision_authority_comes_from_permissions(string roleCode, ApprovalTier tier, bool canOverride)
    {
        Assert.Equal(new DeciderAuthority(tier, canOverride), CurrentAccessProfile.AuthorityOf(Profile(roleCode)));
    }

    [Theory]
    [InlineData(RoleCodes.Operator)]
    [InlineData(RoleCodes.CreditCommittee)]
    [InlineData(RoleCodes.Auditor)]
    public void Other_roles_have_no_underwriting_authority(string roleCode)
    {
        Assert.Null(CurrentAccessProfile.AuthorityOf(Profile(roleCode, roleCode == RoleCodes.Operator ? Vake : null)));
    }

    private static UserAccessProfile Profile(string roleCode, BranchId? branch = null) =>
        TestReferenceData.AccessProfile(TestReferenceData.Staff(roleCode, branch));
}
