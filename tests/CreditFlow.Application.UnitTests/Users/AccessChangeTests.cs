using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.UnitTests.Users;

/// <summary>
/// Every change to an access-relevant attribute (role, active flag, branch, segment, authority level) rotates the
/// security stamp inside the transaction and invalidates the cached access profile after commit. Other edits do neither.
/// </summary>
public sealed class AccessChangeTests
{
    private readonly UserAdministrationFixture _fixture = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Changing_the_role_invalidates_sessions_and_cache()
    {
        var member = _fixture.AddSeniorUnderwriter();
        var command = _fixture.UnchangedUpdate(member) with
        {
            RoleId = TestReferenceData.RoleId(RoleCodes.BranchManager),
        };

        var result = await _fixture.Service.UpdateAsync(command, Ct);

        AssertAccessChangeHandled(result, member);
        Assert.Equal(TestReferenceData.RoleId(RoleCodes.BranchManager), member.RoleId);
        Assert.Null(member.AuthorityLevel);
    }

    [Fact]
    public async Task Changing_the_branch_invalidates_sessions_and_cache()
    {
        var member = _fixture.AddSeniorUnderwriter();

        var result = await _fixture.Service.UpdateAsync(
            _fixture.UnchangedUpdate(member) with { BranchId = _fixture.Saburtalo }, Ct);

        AssertAccessChangeHandled(result, member);
        Assert.Equal(_fixture.Saburtalo, member.BranchId);
    }

    [Fact]
    public async Task Changing_the_segment_invalidates_sessions_and_cache()
    {
        var head = _fixture.AddMember(TestReferenceData.Staff(RoleCodes.Head, segment: Segment.Retail), "sophio");

        var result = await _fixture.Service.UpdateAsync(
            _fixture.UnchangedUpdate(head) with { Segment = Segment.Business }, Ct);

        AssertAccessChangeHandled(result, head);
        Assert.Equal(Segment.Business, head.Segment);
    }

    [Fact]
    public async Task Changing_the_authority_level_invalidates_sessions_and_cache()
    {
        var member = _fixture.AddSeniorUnderwriter();

        var result = await _fixture.Service.UpdateAsync(
            _fixture.UnchangedUpdate(member) with { AuthorityLevel = AuthorityLevel.Junior }, Ct);

        AssertAccessChangeHandled(result, member);
        Assert.Equal(AuthorityLevel.Junior, member.AuthorityLevel);
    }

    [Fact]
    public async Task Deactivation_invalidates_sessions_and_cache()
    {
        var member = _fixture.AddSeniorUnderwriter();

        var result = await _fixture.Service.DeactivateAsync(member.Id, Ct);

        AssertAccessChangeHandled(result, member);
        Assert.False(member.IsActive);
    }

    [Fact]
    public async Task Reactivation_invalidates_sessions_and_cache()
    {
        var member = _fixture.AddSeniorUnderwriter();
        member.Deactivate();

        var result = await _fixture.Service.ActivateAsync(member.Id, Ct);

        AssertAccessChangeHandled(result, member);
        Assert.True(member.IsActive);
    }

    [Fact]
    public async Task Editing_only_name_capacity_user_name_and_email_does_not_touch_sessions_or_cache()
    {
        var member = _fixture.AddSeniorUnderwriter();

        var result = await _fixture.Service.UpdateAsync(
            _fixture.UnchangedUpdate(member) with
            {
                FullName = "Tamta T.",
                Capacity = 15,
                UserName = "tamta.t",
                Email = "tamta.t@pcb.ge",
            },
            Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("Tamta T.", member.FullName);
        Assert.Equal("tamta.t", _fixture.Identity.Accounts[member.Id].UserName);
        Assert.Empty(_fixture.Identity.SessionInvalidations);
        Assert.Empty(_fixture.Cache.InvalidatedUsers);
        Assert.Equal(1, _fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Toggling_availability_does_not_touch_sessions_or_cache()
    {
        var member = _fixture.AddSeniorUnderwriter();

        var result = await _fixture.Service.SetAvailabilityAsync(member.Id, false, Ct);

        Assert.True(result.IsSuccess);
        Assert.False(member.IsAvailable);
        Assert.Empty(_fixture.Identity.SessionInvalidations);
        Assert.Empty(_fixture.Cache.InvalidatedUsers);
    }

    [Fact]
    public async Task Cache_is_not_invalidated_when_the_transaction_rolls_back()
    {
        var member = _fixture.AddSeniorUnderwriter();
        _fixture.Identity.FailUpdatesWith = Error.Conflict("Test.Failure", "Simulated failure.");

        var result = await _fixture.Service.UpdateAsync(
            _fixture.UnchangedUpdate(member) with { BranchId = _fixture.Saburtalo }, Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(1, _fixture.UnitOfWork.Rollbacks);
        Assert.Empty(_fixture.Cache.InvalidatedUsers);
        Assert.Empty(_fixture.Identity.SessionInvalidations);
    }

    private void AssertAccessChangeHandled(Result result, StaffMember member)
    {
        Assert.True(result.IsSuccess, result.Error?.Description);
        Assert.Equal([member.Id], _fixture.Identity.SessionInvalidations);
        Assert.Equal([member.Id], _fixture.Cache.InvalidatedUsers);
        Assert.Equal(1, _fixture.UnitOfWork.Commits);
        Assert.False(_fixture.Identity.WroteOutsideTransaction);
        Assert.False(_fixture.Cache.InvalidatedInsideTransaction);
    }
}
