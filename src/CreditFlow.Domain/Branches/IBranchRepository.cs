namespace CreditFlow.Domain.Branches;

/// <summary>Persistence of the <see cref="Branch"/> aggregate.</summary>
public interface IBranchRepository
{
    Task<Branch?> GetAsync(BranchId id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>Staff members (active or not) assigned to the branch (see <see cref="Branch.EnsureCanBeDeleted"/>).</summary>
    Task<int> CountAssignedStaffAsync(BranchId id, CancellationToken cancellationToken);

    void Add(Branch branch);

    void Remove(Branch branch);
}
