using CreditFlow.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CreditFlow.Web.Authorization;

/// <summary>Satisfied when the user holds at least one of <see cref="Permissions"/>.</summary>
public sealed class PermissionRequirement(IReadOnlyList<string> permissions) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}

/// <summary>Satisfied when the user holds at least one <c>admin.*</c> permission.</summary>
public sealed class AdministrationRequirement : IAuthorizationRequirement
{
    public static readonly AdministrationRequirement Instance = new();
}

/// <summary>
/// Builds permission policies on demand from their name (<c>perm:{code}</c>, <c>perm-any:{a,b}</c>), so no policy
/// has to be registered per permission. Unknown permission codes fail fast at the first request.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        string[]? permissions = null;
        if (policyName.StartsWith(Policies.AnyPermissionPrefix, StringComparison.Ordinal))
        {
            permissions = policyName[Policies.AnyPermissionPrefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        else if (policyName.StartsWith(Policies.PermissionPrefix, StringComparison.Ordinal))
        {
            permissions = [policyName[Policies.PermissionPrefix.Length..]];
        }

        if (permissions is null)
        {
            return await base.GetPolicyAsync(policyName);
        }

        var unknown = permissions.FirstOrDefault(p => !PermissionCatalog.Contains(p));
        if (permissions.Length == 0 || unknown is not null)
        {
            throw new InvalidOperationException($"Policy '{policyName}' refers to an unknown permission '{unknown}'.");
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissions))
            .Build();
    }
}

public sealed class PermissionHandler(CurrentAccess currentAccess) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var profile = await currentAccess.GetAsync();
        if (profile is not null && requirement.Permissions.Any(profile.HasPermission))
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class AdministrationHandler(CurrentAccess currentAccess) : AuthorizationHandler<AdministrationRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdministrationRequirement requirement)
    {
        var profile = await currentAccess.GetAsync();
        if (profile?.HasAnyAdministrativePermission == true)
        {
            context.Succeed(requirement);
        }
    }
}
