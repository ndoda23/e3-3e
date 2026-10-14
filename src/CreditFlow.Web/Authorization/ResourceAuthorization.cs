using CreditFlow.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace CreditFlow.Web.Authorization;

/// <summary>
/// Resource-based requirements. Usage: <c>await authorizationService.AuthorizeAsync(User, record,
/// ResourceRequirements.DataScope)</c>. The decisions themselves live in the Application layer evaluators.
/// </summary>
public static class ResourceRequirements
{
    /// <summary>The record is within the user's data scope.</summary>
    public static readonly DataScopeRequirement DataScope = new();
}

public sealed class DataScopeRequirement : IAuthorizationRequirement;

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
