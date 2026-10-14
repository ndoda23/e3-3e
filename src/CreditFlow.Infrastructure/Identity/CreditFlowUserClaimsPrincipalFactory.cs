using System.Security.Claims;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreditFlow.Infrastructure.Identity;

/// <summary>
/// The only claims principal factory. The cookie carries stable identity claims only: user id, user name, security
/// stamp and the role id. No role names, permissions or branch (those come from the cached access
/// profile on every request). <c>SecurityStampValidator</c> refreshes the principal through this same factory.
/// </summary>
public sealed class CreditFlowUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> optionsAccessor,
    CreditFlowDbContext db)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var claimTypes = Options.ClaimsIdentity;

        var identity = new ClaimsIdentity(
            IdentityConstants.ApplicationScheme, claimTypes.UserNameClaimType, claimTypes.RoleClaimType);

        identity.AddClaim(new Claim(claimTypes.UserIdClaimType, user.Id.ToString()));
        identity.AddClaim(new Claim(claimTypes.UserNameClaimType, user.UserName ?? string.Empty));

        if (UserManager.SupportsUserSecurityStamp)
        {
            identity.AddClaim(new Claim(claimTypes.SecurityStampClaimType, await UserManager.GetSecurityStampAsync(user)));
        }

        var staffId = new UserId(user.Id);
        var roleIds = await db.StaffMembers
            .Where(s => s.Id == staffId)
            .Select(s => s.RoleId)
            .ToListAsync();
        if (roleIds.Count == 1)
        {
            identity.AddClaim(new Claim(CreditFlowClaimTypes.RoleId, roleIds[0].ToString()));
        }

        return identity;
    }
}
