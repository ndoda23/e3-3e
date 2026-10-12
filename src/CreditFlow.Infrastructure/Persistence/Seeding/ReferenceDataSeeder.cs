using CreditFlow.Application.ReferenceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CreditFlow.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds reference data in every environment:
/// <list type="number">
/// <item>Runs each pending <see cref="ISeedUnit"/> once, in order, in its own transaction, and records it in
/// <c>SeedHistory</c>. A unit that has run is never run again, so administrator changes (permissions, limits,
/// branches) are never overwritten.</item>
/// <item>Re-applies the code-owned part of every known role (name, description, data scope, dashboard, attribute
/// rules, administrative flag). Permissions and the MFA flag are not touched.</item>
/// </list>
/// </summary>
internal sealed partial class ReferenceDataSeeder(CreditFlowDbContext db, TimeProvider clock, ILogger<ReferenceDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await ApplyPendingUnitsAsync(cancellationToken);
        await ApplyFixedRoleDefinitionsAsync(cancellationToken);
    }

    private async Task ApplyPendingUnitsAsync(CancellationToken cancellationToken)
    {
        var applied = (await db.SeedHistory.Select(s => s.SeedId).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var unit in SeedUnits.All.Where(u => !applied.Contains(u.Id)))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await unit.ApplyAsync(db, cancellationToken);
            db.SeedHistory.Add(new SeedHistoryEntry { SeedId = unit.Id, AppliedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            LogSeedApplied(unit.Id);
        }
    }

    private async Task ApplyFixedRoleDefinitionsAsync(CancellationToken cancellationToken)
    {
        var roles = await db.Roles.ToListAsync(cancellationToken);
        foreach (var role in roles)
        {
            var seed = RoleSeedCatalog.All.SingleOrDefault(r => r.Code == role.Code);
            if (seed is not null)
            {
                SeedOperations.ApplyFixedDefinition(role, seed);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied reference-data seed unit {SeedId}")]
    private partial void LogSeedApplied(string seedId);
}
