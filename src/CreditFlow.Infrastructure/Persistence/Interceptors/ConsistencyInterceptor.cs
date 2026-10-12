using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CreditFlow.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Keeps derived data consistent inside the same SaveChanges (and so the same transaction):
/// <list type="bullet">
/// <item>audit records can never be updated or deleted;</item>
/// <item><c>AspNetUserRoles</c> is rewritten to exactly one row whenever <c>StaffMember.RoleId</c> is set or changed;</item>
/// <item>a replaced password hash is moved to <c>UserPasswordHistory</c> (trimmed to the policy depth).</item>
/// </list>
/// </summary>
public sealed class ConsistencyInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            ApplyAsync(context, async: false, CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            await ApplyAsync(context, async: true, cancellationToken);
        }

        return result;
    }

    private async Task ApplyAsync(DbContext context, bool async, CancellationToken cancellationToken)
    {
        context.ChangeTracker.DetectChanges();

        GuardAuditLog(context);
        await SynchronizeUserRolesAsync(context, async, cancellationToken);
        await RecordPasswordHistoryAsync(context, async, cancellationToken);
    }

    private static void GuardAuditLog(DbContext context)
    {
        if (context.ChangeTracker.Entries<AuditLog>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit records are append-only and cannot be updated or deleted.");
        }
    }

    private static async Task SynchronizeUserRolesAsync(DbContext context, bool async, CancellationToken cancellationToken)
    {
        var changed = context.ChangeTracker.Entries<StaffMember>()
            .Where(e => e.State == EntityState.Added
                        || (e.State == EntityState.Modified && e.Property(s => s.RoleId).IsModified
                            && !Equals(e.Property(s => s.RoleId).OriginalValue, e.Property(s => s.RoleId).CurrentValue)))
            .Select(e => e.Entity)
            .ToList();

        if (changed.Count == 0)
        {
            return;
        }

        var userRoles = context.Set<IdentityUserRole<Guid>>();
        foreach (var staff in changed)
        {
            var userId = staff.Id.Value;
            var roleId = staff.RoleId.Value;

            var stored = async
                ? await userRoles.Where(ur => ur.UserId == userId).ToListAsync(cancellationToken)
                : userRoles.Where(ur => ur.UserId == userId).ToList();
            var current = stored.Union(userRoles.Local.Where(ur => ur.UserId == userId)).ToList();

            foreach (var other in current.Where(ur => ur.RoleId != roleId))
            {
                userRoles.Remove(other);
            }

            if (!current.Any(ur => ur.RoleId == roleId))
            {
                userRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
            }
        }
    }

    private async Task RecordPasswordHistoryAsync(DbContext context, bool async, CancellationToken cancellationToken)
    {
        var replaced = context.ChangeTracker.Entries<ApplicationUser>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => new
            {
                e.Entity.Id,
                Old = e.Property(u => u.PasswordHash).OriginalValue,
                New = e.Property(u => u.PasswordHash).CurrentValue,
            })
            .Where(x => x.Old is not null && x.Old != x.New)
            .ToList();

        if (replaced.Count == 0)
        {
            return;
        }

        var history = context.Set<UserPasswordHistory>();
        var keep = PasswordHistoryValidator.StoredHistoryDepth;
        foreach (var change in replaced)
        {
            history.Add(new UserPasswordHistory
            {
                UserId = change.Id,
                PasswordHash = change.Old!,
                CreatedAt = clock.GetUtcNow(),
            });

            // Keep only the newest entries (the one added above counts as the newest).
            var query = history.Where(h => h.UserId == change.Id)
                .OrderByDescending(h => h.CreatedAt)
                .ThenByDescending(h => h.Id)
                .Skip(keep - 1);
            var obsolete = async ? await query.ToListAsync(cancellationToken) : query.ToList();
            history.RemoveRange(obsolete);
        }
    }
}
