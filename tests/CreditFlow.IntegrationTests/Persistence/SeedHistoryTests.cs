using System.Text.RegularExpressions;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.Infrastructure.Persistence;
using CreditFlow.Infrastructure.Persistence.Seeding;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.IntegrationTests.Persistence;

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
            [
                "0001_initial_roles_permissions", "0002_initial_branches", "0003_initial_authority_limits",
                "0004_creditflow_roles", "0005_lending_reference_data",
            ],
            applied);
    }

    [Fact]
    public async Task A_fresh_database_has_exactly_the_creditflow_roles_with_the_seeded_matrix()
    {
        var roles = await Host.QueryAsync(db => db.Roles.Include(r => r.Permissions).ToListAsync(Ct));

        Assert.Equal(
            RoleSeedCatalog.All.Select(r => r.Code).Order(StringComparer.Ordinal),
            roles.Select(r => r.Code).Order(StringComparer.Ordinal));
        Assert.All(roles, role => Assert.Equal(
            RoleSeedCatalog.Get(role.Code).Permissions.Order(StringComparer.Ordinal),
            role.Permissions.Select(p => p.Permission).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task Unit_0004_converts_a_database_that_still_has_the_lams_roles()
    {
        // Simulate a Phase 1 database: unit 0004 has not run, a Loan Officer and a Head still exist, and the kept
        // Underwriter role still holds a LAMS permission.
        var (officerId, headId) = await Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlowDbContext>();
            db.SeedHistory.Remove(await db.SeedHistory.SingleAsync(s => s.SeedId == "0004_creditflow_roles", Ct));
            var underwriter = await db.Roles.SingleAsync(r => r.Code == RoleCodes.Underwriter, Ct);
            underwriter.Permissions.Add(new RolePermission { RoleId = underwriter.Id, Permission = "applications.decide" });
            var officerRole = AddLamsRole(db, "LO", AttributeRule.Required);
            var headRole = AddLamsRole(db, "HD", AttributeRule.NotAllowed);
            await db.SaveChangesAsync(Ct);

            var vake = await db.Branches.Where(b => b.Code == BranchSeedCatalog.Vake).Select(b => b.Id).SingleAsync(Ct);
            var officer = await AddStaffAsync(sp, "lams.officer", officerRole.ToProfile(), vake);
            var head = await AddStaffAsync(sp, "lams.head", headRole.ToProfile(), null);
            return (officer, head);
        });

        await RestartAsync();

        var (codes, underwriterPermissions, officer, head) = await Host.QueryAsync(async db => (
            await db.Roles.Select(r => r.Code).ToListAsync(Ct),
            await db.RolePermissions
                .Where(p => db.Roles.Any(r => r.Id == p.RoleId && r.Code == RoleCodes.Underwriter))
                .Select(p => p.Permission)
                .ToListAsync(Ct),
            await db.UserDirectory.SingleAsync(u => u.Id == officerId.Value, Ct),
            await db.UserDirectory.SingleAsync(u => u.Id == headId.Value, Ct)));

        Assert.DoesNotContain("LO", codes);
        Assert.DoesNotContain("HD", codes);
        Assert.Equal(
            RoleSeedCatalog.Get(RoleCodes.Underwriter).Permissions.Order(StringComparer.Ordinal),
            underwriterPermissions.Order(StringComparer.Ordinal));

        Assert.Equal(RoleCodes.Operator, officer.RoleCode);
        Assert.Equal("Vake", officer.BranchName);
        Assert.True(officer.IsActive);

        Assert.Equal(RoleCodes.Auditor, head.RoleCode);
        Assert.False(head.IsActive);
    }

    [Fact]
    public async Task Deleted_branches_and_revoked_permissions_do_not_come_back_on_restart()
    {
        await Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlowDbContext>();
            db.Branches.Remove(await db.Branches.SingleAsync(b => b.Code == BranchSeedCatalog.Rustavi, Ct));
            var compliance = await db.Roles.SingleAsync(r => r.Code == RoleCodes.Compliance, Ct);
            db.RolePermissions.RemoveRange(await db.RolePermissions.Where(p => p.RoleId == compliance.Id).ToListAsync(Ct));
            await db.SaveChangesAsync(Ct);
        });

        await RestartAsync();

        var (rustavi, compliancePermissions) = await Host.QueryAsync(async db => (
            await db.Branches.AnyAsync(b => b.Code == BranchSeedCatalog.Rustavi, Ct),
            await db.RolePermissions.CountAsync(p => db.Roles.Any(r => r.Id == p.RoleId && r.Code == RoleCodes.Compliance), Ct)));
        Assert.False(rustavi);
        Assert.Equal(0, compliancePermissions);
    }

    [Fact]
    public async Task Fixed_role_definitions_are_code_owned_and_restored_on_restart()
    {
        await Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlowDbContext>();
            var role = await db.Roles.SingleAsync(r => r.Code == RoleCodes.Operator, Ct);
            role.DataScope = DataScope.Organization;
            role.DisplayName = "Tampered";
            role.Kind = RoleKind.Administrative;
            role.RequireMfa = true; // administrator-owned: must survive
            await db.SaveChangesAsync(Ct);
        });

        await RestartAsync();

        var role = await Host.QueryAsync(db => db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Code == RoleCodes.Operator, Ct));
        var seed = RoleSeedCatalog.Get(RoleCodes.Operator);
        Assert.Equal(DataScope.Branch, role.DataScope);
        Assert.Equal(seed.DisplayName, role.DisplayName);
        Assert.Equal(RoleKind.Business, role.Kind);
        Assert.True(role.RequireMfa);
        Assert.Equal(seed.Permissions.Order(), role.Permissions.Select(p => p.Permission).Order());
    }

    [Fact]
    public async Task A_seed_grant_is_idempotent_and_respects_segregation_of_duties()
    {
        await Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlowDbContext>();

            await SeedOperations.GrantAsync(db, RoleCodes.Underwriter, [Permissions.Reports.Export], Ct);
            await SeedOperations.GrantAsync(db, RoleCodes.Underwriter, [Permissions.Reports.Export], Ct);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SeedOperations.GrantAsync(db, RoleCodes.Underwriter, [Permissions.Admin.UsersManage], Ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SeedOperations.GrantAsync(db, RoleCodes.Auditor, [Permissions.Applications.Create], Ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SeedOperations.GrantAsync(db, RoleCodes.Underwriter, ["applications.unknown"], Ct));
        });

        var count = await Host.QueryAsync(db => db.RolePermissions.CountAsync(
            p => p.Permission == Permissions.Reports.Export && db.Roles.Any(r => r.Id == p.RoleId && r.Code == RoleCodes.Underwriter),
            Ct));
        Assert.Equal(1, count);
    }

    private Task RestartAsync() => Host.Services.InitializeDatabaseAsync(Ct);

    private static ApplicationRole AddLamsRole(CreditFlowDbContext db, string code, AttributeRule branchRule)
    {
        var role = new ApplicationRole
        {
            Id = RoleId.New().Value,
            Name = code,
            NormalizedName = code,
            Code = code,
            DisplayName = code,
            Description = "LAMS role",
            Kind = RoleKind.Business,
            DataScope = DataScope.Organization,
            Dashboard = DashboardKind.Audit,
            BranchRule = branchRule,
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };
        role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = "applications.view-full" });
        db.Roles.Add(role);
        return role;
    }

    private static async Task<UserId> AddStaffAsync(
        IServiceProvider sp, string userName, RoleProfile role, CreditFlow.Domain.Branches.BranchId? branchId)
    {
        var id = UserId.New();
        var db = sp.GetRequiredService<CreditFlowDbContext>();
        var identity = sp.GetRequiredService<IIdentityService>();
        var result = await sp.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            async token =>
            {
                await identity.CreateAccountAsync(
                    new NewAccount(id, userName, $"{userName}@pcb.ge", "Temp!Passw0rd-123", DateTimeOffset.UtcNow.AddDays(1), false),
                    token);
                db.StaffMembers.Add(StaffMember.Create(id, userName, role, branchId).Value);
                await db.SaveChangesAsync(token);
                return Result.Success();
            },
            Ct);
        Assert.True(result.IsSuccess);
        return id;
    }
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
}
