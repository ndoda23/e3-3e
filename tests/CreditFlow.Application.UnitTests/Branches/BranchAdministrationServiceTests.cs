using CreditFlow.Application.Branches;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Common;

namespace CreditFlow.Application.UnitTests.Branches;

public sealed class BranchAdministrationServiceTests
{
    private readonly FakeReferenceRepositories _branches = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly BranchAdministrationService _service;

    public BranchAdministrationServiceTests()
    {
        _service = new BranchAdministrationService(
            _branches, _branches, _unitOfWork, new CreateBranchCommandValidator(), new UpdateBranchCommandValidator());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creates_a_branch_with_a_normalized_code()
    {
        var result = await _service.CreateAsync(new CreateBranchCommand("gld", "Gldani", "Tbilisi"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("GLD", _branches.Branches[result.Value].Code);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Duplicate_codes_are_rejected()
    {
        _branches.AddBranch("VAK", "Vake");

        var result = await _service.CreateAsync(new CreateBranchCommand("vak", "Vake 2", "Tbilisi"), Ct);

        Assert.Equal(BranchErrors.DuplicateCode, result.Error);
    }

    [Fact]
    public async Task Invalid_input_reports_every_field()
    {
        var result = await _service.CreateAsync(new CreateBranchCommand("x", "", ""), Ct);

        Assert.Equal(Error.ValidationFailedCode, result.Error?.Code);
        Assert.Equal(["Code", "Name", "City"], result.Error!.Details.Select(d => d.Target));
    }

    [Fact]
    public async Task Branch_with_assigned_users_is_deactivated_not_deleted()
    {
        var branch = _branches.AddBranch("VAK", "Vake");
        _branches.AssignedStaffCount = 3;

        var delete = await _service.DeleteAsync(branch.Id, Ct);
        Assert.Equal(BranchErrors.HasAssignedUsers, delete.Error);
        Assert.Contains(branch.Id, _branches.Branches.Keys);

        var deactivate = await _service.SetActiveAsync(branch.Id, false, Ct);
        Assert.True(deactivate.IsSuccess);
        Assert.False(branch.IsActive);
    }

    [Fact]
    public async Task Branch_without_users_can_be_deleted()
    {
        var branch = _branches.AddBranch("TMP", "Temporary");

        var result = await _service.DeleteAsync(branch.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(branch.Id, _branches.Branches.Keys);
    }
}
