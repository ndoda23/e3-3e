using FluentValidation;
using Lams.Application.Common;
using Lams.Domain.Branches;
using Lams.Domain.Common;

namespace Lams.Application.Branches;

/// <summary>Branch administration. Branches with assigned users are deactivated, never deleted.</summary>
public sealed class BranchAdministrationService(
    IBranchRepository branches,
    IUnitOfWork unitOfWork,
    IValidator<CreateBranchCommand> createValidator,
    IValidator<UpdateBranchCommand> updateValidator)
{
    public Task<IReadOnlyList<BranchSummary>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        branches.ListAsync(includeInactive, cancellationToken);

    public Task<Branch?> GetAsync(BranchId id, CancellationToken cancellationToken) =>
        branches.GetAsync(id, cancellationToken);

    public async Task<Result<BranchId>> CreateAsync(CreateBranchCommand command, CancellationToken cancellationToken)
    {
        var errors = await createValidator.ValidateToErrorsAsync(command, cancellationToken);
        if (errors.Count > 0)
        {
            return Result.Failure<BranchId>(Error.ValidationFailed(errors));
        }

        var created = Branch.Create(command.Code, command.Name, command.City);
        if (created.IsFailure)
        {
            return Result.Failure<BranchId>(created.Error!);
        }

        if (await branches.CodeExistsAsync(created.Value.Code, cancellationToken))
        {
            return Result.Failure<BranchId>(BranchErrors.DuplicateCode);
        }

        branches.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(created.Value.Id);
    }

    public async Task<Result> UpdateAsync(UpdateBranchCommand command, CancellationToken cancellationToken)
    {
        var errors = await updateValidator.ValidateToErrorsAsync(command, cancellationToken);
        if (errors.Count > 0)
        {
            return errors.ToResult();
        }

        var branch = await branches.GetAsync(command.Id, cancellationToken);
        if (branch is null)
        {
            return Result.Failure(BranchErrors.NotFound);
        }

        var updated = branch.Update(command.Name, command.City);
        if (updated.IsFailure)
        {
            return updated;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetActiveAsync(BranchId id, bool isActive, CancellationToken cancellationToken)
    {
        var branch = await branches.GetAsync(id, cancellationToken);
        if (branch is null)
        {
            return Result.Failure(BranchErrors.NotFound);
        }

        if (isActive)
        {
            branch.Activate();
        }
        else
        {
            branch.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(BranchId id, CancellationToken cancellationToken)
    {
        var branch = await branches.GetAsync(id, cancellationToken);
        if (branch is null)
        {
            return Result.Failure(BranchErrors.NotFound);
        }

        var assigned = await branches.CountAssignedStaffAsync(id, cancellationToken);
        var canDelete = Branch.EnsureCanBeDeleted(assigned);
        if (canDelete.IsFailure)
        {
            return canDelete;
        }

        branches.Remove(branch);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
