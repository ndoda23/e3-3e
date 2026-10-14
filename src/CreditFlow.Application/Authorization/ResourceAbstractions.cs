using CreditFlow.Domain.Branches;

namespace CreditFlow.Application.Authorization;

/// <summary>A business record whose visibility follows the data scope (the branch it belongs to).</summary>
public interface IScopedResource
{
    /// <summary>The branch the record belongs to; <c>null</c> for records of no branch.</summary>
    BranchId? BranchId { get; }
}
