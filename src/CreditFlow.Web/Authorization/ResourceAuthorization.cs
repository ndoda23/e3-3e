using CreditFlow.Application.Authorization;
using CreditFlow.Domain.AuthorityLimits;
using Microsoft.AspNetCore.Authorization;

namespace CreditFlow.Web.Authorization;

/// <summary>
/// Resource-based requirements for Phase 2. Usage: <c>await authorizationService.AuthorizeAsync(User, application,
/// ResourceRequirements.DataScope)</c>. The decisions themselves live in the Application layer evaluators.
/// </summary>
public static class ResourceRequirements
{
    /// <summary>The record is within the user's data scope.</summary>
    public static readonly DataScopeRequirement DataScope = new();

    /// <summary>The user may approve / decline this amount (within the credit decision limit).</summary>
    public static readonly CreditDecisionRequirement CreditDecision = new();

    /// <summary>The user may approve this pricing exception (own segment, within the pricing exception limit).</summary>
    public static readonly PricingExceptionApprovalRequirement PricingExceptionApproval = new();
}

public sealed class DataScopeRequirement : IAuthorizationRequirement;

public sealed class CreditDecisionRequirement : IAuthorizationRequirement;

public sealed class PricingExceptionApprovalRequirement : IAuthorizationRequirement;

public sealed class DataScopeHandler(CurrentAccess currentAccess, IDataScopeEvaluator evaluator)
    : AuthorizationHandler<DataScopeRequirement, IScopedResource>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, DataScopeRequirement requirement, IScopedResource resource)
    {
        var profile = await currentAccess.GetAsync();
        if (profile is not null && evaluator.IsInScope(profile, resource))
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class CreditDecisionHandler(CurrentAccess currentAccess, IAuthorityLimitEvaluator evaluator)
    : AuthorizationHandler<CreditDecisionRequirement, ICreditDecisionResource>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CreditDecisionRequirement requirement, ICreditDecisionResource resource)
    {
        var profile = await currentAccess.GetAsync();
        if (profile is null)
        {
            return;
        }

        var evaluation = await evaluator.EvaluateCreditDecisionAsync(profile, resource, CancellationToken.None);
        if (evaluation.Outcome == CreditDecisionOutcome.Within)
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class PricingExceptionApprovalHandler(CurrentAccess currentAccess, IAuthorityLimitEvaluator evaluator)
    : AuthorizationHandler<PricingExceptionApprovalRequirement, IPricedResource>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PricingExceptionApprovalRequirement requirement, IPricedResource resource)
    {
        var profile = await currentAccess.GetAsync();
        if (profile is null)
        {
            return;
        }

        var outcome = await evaluator.EvaluatePricingExceptionAsync(profile, resource, CancellationToken.None);
        if (outcome == PricingExceptionOutcome.Within)
        {
            context.Succeed(requirement);
        }
    }
}
