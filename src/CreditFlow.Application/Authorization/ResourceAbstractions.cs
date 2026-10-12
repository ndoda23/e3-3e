using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.Authorization;

/// <summary>
/// A business record that is visible according to data scope (Phase 2: applications, packages, documents).
/// </summary>
public interface IScopedResource
{
    /// <summary>The user who owns (created) the record.</summary>
    UserId? OwnerUserId { get; }

    /// <summary>The user the record is currently assigned to.</summary>
    UserId? AssignedUserId { get; }

    BranchId? BranchId { get; }

    Segment? Segment { get; }
}

/// <summary>A record that needs a credit decision (Phase 2: an application).</summary>
public interface ICreditDecisionResource
{
    decimal Amount { get; }

    Currency Currency { get; }
}

/// <summary>A pricing exception awaiting approval (Phase 2).</summary>
public interface IPricedResource
{
    Segment Segment { get; }

    /// <summary>How far the offered rate is below the policy floor, in percentage points.</summary>
    decimal RateGapPp { get; }

    /// <summary>Customer-level ROAE confirmed by FAB, in percent.</summary>
    decimal CustomerRoaePct { get; }
}
