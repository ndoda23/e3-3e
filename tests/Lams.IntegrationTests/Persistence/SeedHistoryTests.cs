using System.Text.RegularExpressions;
using Lams.Application.Authorization;
using Lams.Application.ReferenceData;
using Lams.Domain.AuthorityLimits;
using Lams.Domain.Roles;
using Lams.Infrastructure.Persistence;
using Lams.Infrastructure.Persistence.Seeding;
using Lams.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lams.IntegrationTests.Persistence;

/// <summary>Versioned reference-data seeding: each unit runs once; administrator changes are never overwritten.</summary>
public sealed class SeedHistoryTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    [Fact]
    public async Task Every_seed_unit_is_recorded_once()
    {
        await RestartAsync();

        var applied = await Host.QueryAsync(db => db.SeedHistory.OrderBy(s => s.SeedId).Select(s => s.SeedId).ToListAsync(Ct));

        Assert.Equal(SeedUnits.All.Select(u => u.Id), applied);
        Assert.Equal(
            ["0001_initial_roles_permissions", "0002_initial_branches", "0003_initial_authority_limits"],
            applied);
    }

    [Fact]
    public async Task Deleted_limits_of_a_type_do_not_come_back_on_restart()
    {
        // The administrator deletes every pricing-exception limit and every credit limit of the underwriter role.
        await Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<LamsDbContext>();
            var uw = new RoleId(await db.Roles.Where(r => r.Code == RoleCodes.Underwriter).Select(r => r.Id).SingleAsync(Ct));
            db.AuthorityLimits.RemoveRange(await db.AuthorityLimits
                .Where(l => l.LimitType == LimitType.PricingException
                            || (l.LimitType == LimitType.CreditDecision && l.RoleId == uw))
                .ToListAsync(Ct));
            await db.SaveChangesAsync(Ct);
        });

        await RestartAsync();
        await RestartAsync();

        var limits = await Host.QueryAsync(db => db.AuthorityLimits.ToListAsync(Ct));
        Assert.DoesNotContain(limits, l => l.LimitType == LimitType.PricingException);
        Assert.Single(limits); // only the Branch Manager credit limit remains
    }

    [Fact]
    public async Task Deleted_branches_and_revoked_permissions_do_not_come_back_on_restart()
    {
        await Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<LamsDbContext>();
            db.Branches.Remove(await db.Branches.SingleAsync(b => b.Code == BranchSeedCatalog.Rustavi, Ct));
            var fab = await db.Roles.SingleAsync(r => r.Code == RoleCodes.FinancialAnalysis, Ct);
            db.RolePermissions.RemoveRange(await db.RolePermissions.Where(p => p.RoleId == fab.Id).ToListAsync(Ct));
            await db.SaveChangesAsync(Ct);
        });

        await RestartAsync();

        var (rustavi, fabPermissions) = await Host.QueryAsync(async db => (
            await db.Branches.AnyAsync(b => b.Code == BranchSeedCatalog.Rustavi, Ct),
            await db.RolePermissions.CountAsync(p => db.Roles.Any(r => r.Id == p.RoleId && r.Code == RoleCodes.FinancialAnalysis), Ct)));
        Assert.False(rustavi);
        Assert.Equal(0, fabPermissions);
    }

    [Fact]
    public async Task Fixed_role_definitions_are_code_owned_and_restored_on_restart()
    {
        await Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<LamsDbContext>();
            var bm = await db.Roles.SingleAsync(r => r.Code == RoleCodes.BranchManager, Ct);
            bm.DataScope = DataScope.Organization;
            bm.DisplayName = "Tampered";
            bm.IsAdministrative = true;
            bm.RequireMfa = true; // administrator-owned: must survive
            await db.SaveChangesAsync(Ct);
        });

        await RestartAsync();

        var role = await Host.QueryAsync(db => db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Code == RoleCodes.BranchManager, Ct));
        var seed = RoleSeedCatalog.Get(RoleCodes.BranchManager);
        Assert.Equal(DataScope.Branch, role.DataScope);
        Assert.Equal(seed.DisplayName, role.DisplayName);
        Assert.False(role.IsAdministrative);
        Assert.True(role.RequireMfa);
        Assert.Equal(seed.Permissions.Order(), role.Permissions.Select(p => p.Permission).Order());
    }

    [Fact]
    public async Task A_seed_grant_is_idempotent_and_respects_segregation_of_duties()
    {
        await Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<LamsDbContext>();

            await SeedOperations.GrantAsync(db, RoleCodes.Underwriter, [Permissions.Applications.Reassign], Ct);
            await SeedOperations.GrantAsync(db, RoleCodes.Underwriter, [Permissions.Applications.Reassign], Ct);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SeedOperations.GrantAsync(db, RoleCodes.Underwriter, [Permissions.Admin.UsersManage], Ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SeedOperations.GrantAsync(db, RoleCodes.Underwriter, ["applications.unknown"], Ct));
        });

        var count = await Host.QueryAsync(db => db.RolePermissions.CountAsync(p => p.Permission == Permissions.Applications.Reassign, Ct));
        Assert.Equal(1, count);
    }

    private Task RestartAsync() => Host.Services.InitializeDatabaseAsync(Ct);
}

/// <summary>Rules for the list of seed units (no database needed).</summary>
public sealed partial class SeedUnitCatalogTests
{
    [GeneratedRegex("^[0-9]{4}_[a-z0-9_]+$")]
    private static partial Regex IdPattern();

    [Fact]
    public void Seed_unit_ids_are_well_formed_unique_and_in_order()
    {
        var ids = SeedUnits.All.Select(u => u.Id).ToList();

        Assert.All(ids, id => Assert.Matches(IdPattern(), id));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        Assert.All(ids, id => Assert.True(id.Length <= SeedHistoryEntry.SeedIdMaxLength));
    }

    [Fact]
    public void The_first_unit_creates_exactly_the_initial_ten_roles()
    {
        Assert.Equal(10, SeedUnits.InitialRoleCodes.Length);
        Assert.Equal(RoleSeedCatalog.All.Select(r => r.Code).Order(), SeedUnits.InitialRoleCodes.Order());
    }
}
