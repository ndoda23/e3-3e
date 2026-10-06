using Lams.Domain.Branches;

namespace Lams.Application.Branches;

/// <summary>A branch with the number of staff members assigned to it.</summary>
public sealed record BranchSummary(BranchId Id, string Code, string Name, string City, bool IsActive, int AssignedUserCount);

public interface IBranchRepository
{
    Task<Branch?> GetAsync(BranchId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<BranchSummary>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>Staff members (active or not) assigned to the branch.</summary>
    Task<int> CountAssignedStaffAsync(BranchId id, CancellationToken cancellationToken);

    void Add(Branch branch);

    void Remove(Branch branch);
}
