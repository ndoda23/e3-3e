using Lams.Application.ReferenceData;
using Lams.Application.Roles;
using Lams.Domain.Roles;
using Lams.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lams.Infrastructure.Persistence.Repositories;

internal sealed class RoleRepository(LamsDbContext db) : IRoleRepository
{
    public async Task<RoleProfile?> GetProfileAsync(RoleId roleId, CancellationToken cancellationToken)
    {
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId.Value, cancellationToken);
        return role?.ToProfile();
    }

    public async Task<RoleDetails?> GetDetailsAsync(RoleId roleId, CancellationToken cancellationToken)
    {
        var role = await db.Roles.AsNoTracking()
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == roleId.Value, cancellationToken);
        if (role is null)
        {
            return null;
        }

        var userCount = await db.StaffMembers.CountAsync(s => s.RoleId == roleId, cancellationToken);
        return ToDetails(role, userCount);
    }

    public async Task<IReadOnlyList<RoleDetails>> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).ToListAsync(cancellationToken);
        var counts = await db.StaffMembers
            .GroupBy(s => s.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId.Value, x => x.Count, cancellationToken);

        // Seed order (LO … DIR, SYSADMIN); any role added later goes last.
        var order = RoleSeedCatalog.All.Select((r, i) => (r.Code, i)).ToDictionary(x => x.Code, x => x.i);
        return roles
            .OrderBy(r => order.GetValueOrDefault(r.Code, int.MaxValue))
            .ThenBy(r => r.DisplayName, StringComparer.Ordinal)
            .Select(r => ToDetails(r, counts.GetValueOrDefault(r.Id)))
            .ToList();
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

    private static RoleDetails ToDetails(ApplicationRole role, int userCount) => new()
    {
        Profile = role.ToProfile(),
        DisplayName = role.DisplayName,
        Description = role.Description,
        IsSystem = role.IsSystem,
        RequireMfa = role.RequireMfa,
        UserCount = userCount,
        Permissions = role.Permissions.Select(p => p.Permission).Order(StringComparer.Ordinal).ToList(),
    };
}
