using CreditFlow.Application.ReferenceData;
using CreditFlow.Application.Roles;
using CreditFlow.Domain.Roles;
using CreditFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Queries;

internal sealed class RoleQueries(CreditFlowDbContext db) : IRoleQueries
{
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

        // Seed order (Operator … Auditor, Admin); any role added later goes last.
        var order = RoleSeedCatalog.All.Select((r, i) => (r.Code, i)).ToDictionary(x => x.Code, x => x.i);
        return roles
            .OrderBy(r => order.GetValueOrDefault(r.Code, int.MaxValue))
            .ThenBy(r => r.DisplayName, StringComparer.Ordinal)
            .Select(r => ToDetails(r, counts.GetValueOrDefault(r.Id)))
            .ToList();
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
