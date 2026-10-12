using CreditFlow.Domain.Branches;

namespace CreditFlow.Application.Branches;

/// <summary>A branch with the number of staff members assigned to it.</summary>
public sealed record BranchSummary(BranchId Id, string Code, string Name, string City, bool IsActive, int AssignedUserCount);

/// <summary>Read side of branch administration.</summary>
public interface IBranchQueries
{
    Task<IReadOnlyList<BranchSummary>> ListAsync(bool includeInactive, CancellationToken cancellationToken);
}
