using CreditFlow.Domain.Roles;
using CreditFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Repositories;

internal sealed class RoleRepository(CreditFlowDbContext db) : IRoleRepository
{
    public async Task<RoleProfile?> GetProfileAsync(RoleId roleId, CancellationToken cancellationToken)
    {
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId.Value, cancellationToken);
        return role?.ToProfile();
    }

    public async Task SetPermissionsAsync(RoleId roleId, PermissionSet permissions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var current = await db.RolePermissions.Where(p => p.RoleId == roleId.Value).ToListAsync(cancellationToken);
        var wanted = permissions.Permissions.Select(p => p.Value).ToHashSet(StringComparer.Ordinal);

        db.RolePermissions.RemoveRange(current.Where(p => !wanted.Contains(p.Permission)));
        foreach (var code in wanted.Except(current.Select(p => p.Permission), StringComparer.Ordinal))
        {
            db.RolePermissions.Add(new RolePermission { RoleId = roleId.Value, Permission = code });
        }
    }

    public async Task SetRequireMfaAsync(RoleId roleId, bool requireMfa, CancellationToken cancellationToken)
    {
        var role = await db.Roles.FirstAsync(r => r.Id == roleId.Value, cancellationToken);
        role.RequireMfa = requireMfa;
    }
}
