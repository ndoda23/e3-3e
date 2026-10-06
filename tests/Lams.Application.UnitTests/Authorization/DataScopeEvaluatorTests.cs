using Lams.Application.Authorization;
using Lams.Application.ReferenceData;
using Lams.Domain.Branches;
using Lams.Domain.Users;

namespace Lams.Application.UnitTests.Authorization;

/// <summary>Data scope checks against fake resources (Phase 2 will pass real applications).</summary>
public sealed class DataScopeEvaluatorTests
{
    private static readonly BranchId Vake = BranchId.New();
    private static readonly BranchId Batumi = BranchId.New();
    private readonly DataScopeEvaluator _evaluator = new();

    private sealed record FakeResource(
        UserId? OwnerUserId = null,
        UserId? AssignedUserId = null,
        BranchId? BranchId = null,
        Segment? Segment = null) : IScopedResource;

    [Fact]
    public void Own_scope_sees_only_records_the_user_owns()
    {
        var officer = Profile(TestReferenceData.Staff(RoleCodes.LoanOfficer, Vake, authorityLevel: AuthorityLevel.Junior));

        Assert.True(_evaluator.IsInScope(officer, new FakeResource(OwnerUserId: officer.UserId)));
        Assert.False(_evaluator.IsInScope(officer, new FakeResource(OwnerUserId: UserId.New(), BranchId: Vake)));
        Assert.False(_evaluator.IsInScope(officer, new FakeResource(AssignedUserId: officer.UserId)));
    }

    [Fact]
    public void Assigned_scope_sees_only_records_assigned_to_the_user()
    {
        var underwriter = Profile(TestReferenceData.Staff(RoleCodes.Underwriter, Vake, authorityLevel: AuthorityLevel.Senior));

        Assert.True(_evaluator.IsInScope(underwriter, new FakeResource(AssignedUserId: underwriter.UserId)));
        Assert.False(_evaluator.IsInScope(underwriter, new FakeResource(AssignedUserId: UserId.New(), BranchId: Vake)));
    }

    [Fact]
    public void Branch_scope_sees_all_records_of_the_users_branch()
    {
        var manager = Profile(TestReferenceData.Staff(RoleCodes.BranchManager, Vake));

        Assert.True(_evaluator.IsInScope(manager, new FakeResource(OwnerUserId: UserId.New(), BranchId: Vake)));
        Assert.False(_evaluator.IsInScope(manager, new FakeResource(BranchId: Batumi)));
        Assert.False(_evaluator.IsInScope(manager, new FakeResource()));
    }

    [Fact]
    public void Segment_scope_sees_only_the_users_segment()
    {
        var headOfRetail = Profile(TestReferenceData.Staff(RoleCodes.Head, segment: Segment.Retail));

        Assert.True(_evaluator.IsInScope(headOfRetail, new FakeResource(Segment: Segment.Retail)));
        Assert.False(_evaluator.IsInScope(headOfRetail, new FakeResource(Segment: Segment.Business)));
        Assert.False(_evaluator.IsInScope(headOfRetail, new FakeResource()));
    }

    [Fact]
    public void Organization_scope_sees_everything()
    {
        var director = Profile(TestReferenceData.Staff(RoleCodes.Director));

        Assert.True(_evaluator.IsInScope(director, new FakeResource(BranchId: Batumi, Segment: Segment.Business)));
    }

    [Fact]
    public void System_administrator_sees_no_business_data()
    {
        var admin = Profile(TestReferenceData.Staff(RoleCodes.SystemAdministrator));

        Assert.False(_evaluator.IsInScope(admin, new FakeResource(OwnerUserId: admin.UserId)));
    }

    [Fact]
    public void Inactive_user_sees_nothing()
    {
        var staff = TestReferenceData.Staff(RoleCodes.Director);
        staff.Deactivate();

        Assert.False(_evaluator.IsInScope(Profile(staff), new FakeResource()));
    }

    [Fact]
    public void Scope_with_a_missing_user_attribute_fails_closed()
    {
        // A branch-scoped profile without a branch must never match records that also have no branch.
        var manager = Profile(TestReferenceData.Staff(RoleCodes.BranchManager, Vake)) with { BranchId = null };
        var head = Profile(TestReferenceData.Staff(RoleCodes.Head, segment: Segment.Retail)) with { Segment = null };

        Assert.False(_evaluator.IsInScope(manager, new FakeResource()));
        Assert.False(_evaluator.IsInScope(head, new FakeResource()));
    }

    private static UserAccessProfile Profile(StaffMember staff) => TestReferenceData.AccessProfile(staff);
}
