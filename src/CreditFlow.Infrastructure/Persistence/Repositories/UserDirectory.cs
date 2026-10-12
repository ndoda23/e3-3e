using CreditFlow.Application.Common;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Repositories;

/// <summary>Read side of user administration, over the <c>vw_UserDirectory</c> view.</summary>
internal sealed class UserDirectory(CreditFlowDbContext db, TimeProvider clock) : IUserDirectory
{
    public async Task<PagedResult<UserListItem>> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query = query.Normalize();
        var now = clock.GetUtcNow();

        var rows = db.UserDirectory.AsNoTracking();

        if (query.Search is { } search)
        {
            rows = rows.Where(u => u.FullName.Contains(search) || u.UserName.Contains(search) || u.Email.Contains(search));
        }

        if (query.RoleId is { } roleId)
        {
            rows = rows.Where(u => u.RoleId == roleId.Value);
        }

        if (query.BranchId is { } branchId)
        {
            rows = rows.Where(u => u.BranchId == branchId.Value);
        }

        rows = query.Status switch
        {
            UserStatusFilter.Active => rows.Where(u => u.IsActive),
            UserStatusFilter.Inactive => rows.Where(u => !u.IsActive),
            UserStatusFilter.LockedOut => rows.Where(u => u.LockoutEnd != null && u.LockoutEnd > now),
            _ => rows,
        };

        rows = (query.Sort, query.Descending) switch
        {
            (UserSortField.Role, false) => rows.OrderBy(u => u.RoleName).ThenBy(u => u.FullName),
            (UserSortField.Role, true) => rows.OrderByDescending(u => u.RoleName).ThenBy(u => u.FullName),
            (UserSortField.Branch, false) => rows.OrderBy(u => u.BranchName).ThenBy(u => u.FullName),
            (UserSortField.Branch, true) => rows.OrderByDescending(u => u.BranchName).ThenBy(u => u.FullName),
            (UserSortField.Status, false) => rows.OrderByDescending(u => u.IsActive).ThenBy(u => u.FullName),
            (UserSortField.Status, true) => rows.OrderBy(u => u.IsActive).ThenBy(u => u.FullName),
            (UserSortField.LastLogin, false) => rows.OrderBy(u => u.LastLoginAt).ThenBy(u => u.FullName),
            (UserSortField.LastLogin, true) => rows.OrderByDescending(u => u.LastLoginAt).ThenBy(u => u.FullName),
            (_, true) => rows.OrderByDescending(u => u.FullName),
            _ => rows.OrderBy(u => u.FullName),
        };

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var items = page.Select(u => new UserListItem(
                new UserId(u.Id),
                u.FullName,
                u.UserName,
                u.Email,
                new RoleId(u.RoleId),
                u.RoleCode,
                u.RoleName,
                u.BranchName,
                u.IsActive,
                u.IsAvailable,
                u.LockoutEnd > now,
                u.LastLoginAt))
            .ToList();

        return new PagedResult<UserListItem>(items, total, query.Page, query.PageSize);
    }

    public async Task<UserDetails?> GetDetailsAsync(UserId id, CancellationToken cancellationToken)
    {
        var u = await db.UserDirectory.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id.Value, cancellationToken);
        if (u is null)
        {
            return null;
        }

        return new UserDetails(
            id,
            u.FullName,
            u.UserName,
            u.Email,
            new RoleId(u.RoleId),
            u.RoleCode,
            u.RoleName,
            u.RoleIsAdministrative,
            u.RoleRequiresMfa,
            u.BranchId is { } branchId ? new BranchId(branchId) : null,
            u.BranchName,
            u.IsActive,
            u.IsAvailable,
            u.Capacity,
            u.LockoutEnd > clock.GetUtcNow(),
            u.LockoutEnd,
            u.TwoFactorEnabled,
            u.RequireMfa,
            u.MustChangePassword,
            u.TemporaryPasswordExpiresAt,
            u.LastLoginAt,
            u.CreatedAt,
            u.UpdatedAt);
    }
}
