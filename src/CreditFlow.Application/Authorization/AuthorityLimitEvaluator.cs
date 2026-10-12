using CreditFlow.Application.AuthorityLimits;
using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Roles;

namespace CreditFlow.Application.Authorization;

/// <summary>Applies the authority limits (data, editable by administrators) to a user and a record.</summary>
public interface IAuthorityLimitEvaluator
{
    Task<CreditDecisionEvaluation> EvaluateCreditDecisionAsync(
        UserAccessProfile user, ICreditDecisionResource resource, CancellationToken cancellationToken);

    Task<PricingExceptionOutcome> EvaluatePricingExceptionAsync(
        UserAccessProfile user, IPricedResource resource, CancellationToken cancellationToken);

    /// <summary>
    /// The user's own credit decision limits, one per currency that has a matching limit row (for display, e.g. on
    /// the dashboard). Empty when the user may not decide or no limit row matches (decisions are then escalated).
    /// </summary>
    Task<IReadOnlyList<CreditDecisionLimit>> ListCreditDecisionLimitsAsync(
        UserAccessProfile user, CancellationToken cancellationToken);
}

/// <summary>The maximum amount a user may approve in one currency.</summary>
public sealed record CreditDecisionLimit(Currency Currency, decimal MaxAmount);

public sealed class AuthorityLimitEvaluator(IAuthorityLimitRepository limits) : IAuthorityLimitEvaluator
{
    public async Task<CreditDecisionEvaluation> EvaluateCreditDecisionAsync(
        UserAccessProfile user, ICreditDecisionResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(resource);

        var canDecide = user.HasPermission(Permissions.Applications.Decide);
        var roleLimits = canDecide
            ? await limits.ListForRoleAsync(user.RoleId, cancellationToken)
            : [];

        return CreditDecisionAuthority.Evaluate(
            canDecide, user.RoleId, user.AuthorityLevel, roleLimits, resource.Currency, resource.Amount);
    }

    public async Task<PricingExceptionOutcome> EvaluatePricingExceptionAsync(
        UserAccessProfile user, IPricedResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(resource);

        var canApprove = user.HasPermission(Permissions.Pricing.ApproveException);
        if (!canApprove)
        {
            return PricingExceptionOutcome.NotAuthorized;
        }

        // A segment-scoped approver (Head) handles only their own segment; without a segment they handle nothing.
        var restrictedToSegment = user.DataScope == DataScope.Segment ? user.Segment : null;
        if (user.DataScope == DataScope.Segment && restrictedToSegment is null)
        {
            return PricingExceptionOutcome.NotAuthorized;
        }

        var roleLimits = await limits.ListForRoleAsync(user.RoleId, cancellationToken);

        return PricingExceptionAuthority.Evaluate(
            canApprove,
            user.RoleId,
            restrictedToSegment,
            roleLimits,
            new PricingExceptionRequest(resource.Segment, resource.RateGapPp, resource.CustomerRoaePct));
    }

    public async Task<IReadOnlyList<CreditDecisionLimit>> ListCreditDecisionLimitsAsync(
        UserAccessProfile user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!user.HasPermission(Permissions.Applications.Decide))
        {
            return [];
        }

        // The same matching rule as EvaluateCreditDecisionAsync, applied to every currency.
        var roleLimits = await limits.ListForRoleAsync(user.RoleId, cancellationToken);
        return Enum.GetValues<Currency>()
            .Select(currency => roleLimits.FirstOrDefault(
                l => l.AppliesToCreditDecision(user.RoleId, user.AuthorityLevel, currency)) is { MaxAmount: { } max }
                ? new CreditDecisionLimit(currency, max)
                : null)
            .OfType<CreditDecisionLimit>()
            .ToList();
    }
}
