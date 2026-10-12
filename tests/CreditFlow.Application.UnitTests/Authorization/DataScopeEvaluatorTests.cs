using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.UnitTests.Authorization;

/// <summary>Data scope checks for a single record (lists apply the same rules in SQL).</summary>
public sealed class DataScopeEvaluatorTests
{
    private static readonly BranchId Vake = BranchId.New();
    private static readonly BranchId Batumi = BranchId.New();
    private readonly DataScopeEvaluator _evaluator = new();

    private sealed record FakeResource(BranchId? BranchId = null) : IScopedResource;

    [Fact]
    public void Branch_scope_sees_all_records_of_the_users_branch_only()
    {
        var operatorInVake = Profile(TestReferenceData.Staff(RoleCodes.Operator, Vake));

        Assert.True(_evaluator.IsInScope(operatorInVake, new FakeResource(Vake)));
        Assert.False(_evaluator.IsInScope(operatorInVake, new FakeResource(Batumi)));
        Assert.False(_evaluator.IsInScope(operatorInVake, new FakeResource()));
    }

    [Theory]
    [InlineData(RoleCodes.Underwriter)]
    [InlineData(RoleCodes.SeniorUnderwriter)]
    [InlineData(RoleCodes.CreditCommittee)]
    [InlineData(RoleCodes.Compliance)]
    [InlineData(RoleCodes.Auditor)]
    public void Organization_scope_sees_every_branch(string roleCode)
    {
        var profile = Profile(TestReferenceData.Staff(roleCode));

        Assert.True(_evaluator.IsInScope(profile, new FakeResource(Batumi)));
        Assert.True(_evaluator.IsInScope(profile, new FakeResource()));
    }

    [Fact]
    public void Admin_sees_no_business_data()
    {
        var admin = Profile(TestReferenceData.Staff(RoleCodes.Admin));

        Assert.False(_evaluator.IsInScope(admin, new FakeResource(Vake)));
        Assert.False(_evaluator.IsInScope(admin, new FakeResource()));
    }

    [Fact]
    public void Inactive_user_sees_nothing()
    {
        var staff = TestReferenceData.Staff(RoleCodes.Auditor);
        staff.Deactivate();

        Assert.False(_evaluator.IsInScope(Profile(staff), new FakeResource()));
    }

    [Fact]
    public void Branch_scope_without_a_branch_fails_closed()
    {
        // A branch-scoped profile without a branch must never match records that also have no branch.
        var operatorWithoutBranch = Profile(TestReferenceData.Staff(RoleCodes.Operator, Vake)) with { BranchId = null };

        Assert.False(_evaluator.IsInScope(operatorWithoutBranch, new FakeResource()));
    }

    private static UserAccessProfile Profile(StaffMember staff) => TestReferenceData.AccessProfile(staff);
}
