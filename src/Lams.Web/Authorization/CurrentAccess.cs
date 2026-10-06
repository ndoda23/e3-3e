using System.Security.Claims;
using Lams.Application.Authorization;
using Lams.Domain.Users;

namespace Lams.Web.Authorization;

/// <summary>
/// The access profile of the signed-in user for the current request, resolved once per request from the cached
/// profile service (never from cookie claims). <c>null</c> for anonymous users and users without a staff record.
/// </summary>
public sealed class CurrentAccess(IHttpContextAccessor accessor, IUserAccessProfileService profiles)
{
    private Task<UserAccessProfile?>? _profile;

    public Task<UserAccessProfile?> GetAsync() => _profile ??= LoadAsync();

    private async Task<UserAccessProfile?> LoadAsync()
    {
        var context = accessor.HttpContext;
        if (context?.User is not { Identity.IsAuthenticated: true } user
            || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            return null;
        }

        return await profiles.GetAsync(new UserId(id), context.RequestAborted);
    }
}
