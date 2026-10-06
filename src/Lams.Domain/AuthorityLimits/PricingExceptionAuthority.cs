using Lams.Domain.Roles;
using Lams.Domain.Users;

namespace Lams.Domain.AuthorityLimits;

public enum PricingExceptionOutcome
{
    /// <summary>The user may approve or decline the exception.</summary>
    Within = 1,

    /// <summary>The exception is outside the user's limit (or no limit is defined) and goes to the Director.</summary>
    RouteToDirector = 2,

    /// <summary>The user may not approve this exception (no permission, or another segment).</summary>
    NotAuthorized = 3,
}

/// <param name="Segment">Segment of the customer the exception belongs to.</param>
/// <param name="RateGapPp">How far the offered rate is below the policy floor, in percentage points (0 or more).</param>
/// <param name="CustomerRoaePct">Customer-level ROAE confirmed by FAB, in percent.</param>
public sealed record PricingExceptionRequest(Segment Segment, decimal RateGapPp, decimal CustomerRoaePct);

/// <summary>
/// Pricing exception authority (FRD Flow B). A Head approves only exceptions of their own segment and only within the
/// limit (gap &lt; max gap AND ROAE ≥ min ROAE); otherwise the exception routes to the Director, who has no limit.
/// </summary>
public static class PricingExceptionAuthority
{
    /// <param name="canApprove">Whether the user holds the approve-exception permission.</param>
    /// <param name="roleId">The user's role.</param>
    /// <param name="restrictedToSegment">The only segment the user may handle, or <c>null</c> for all segments.</param>
    /// <param name="limits">Authority limits (any role; filtered here).</param>
    /// <param name="request">The exception to evaluate.</param>
    public static PricingExceptionOutcome Evaluate(
        bool canApprove,
        RoleId roleId,
        Segment? restrictedToSegment,
        IEnumerable<AuthorityLimit> limits,
        PricingExceptionRequest request)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.RateGapPp);

        if (!canApprove)
        {
            return PricingExceptionOutcome.NotAuthorized;
        }

        if (restrictedToSegment is { } segment && segment != request.Segment)
        {
            return PricingExceptionOutcome.NotAuthorized;
        }

        var limit = limits.FirstOrDefault(l => l.AppliesToPricingException(roleId));
        if (limit is null)
        {
            return PricingExceptionOutcome.RouteToDirector;
        }

        return limit.AllowsPricingException(request.RateGapPp, request.CustomerRoaePct)
            ? PricingExceptionOutcome.Within
            : PricingExceptionOutcome.RouteToDirector;
    }
}
