using CreditFlow.Application.ReferenceData;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.Infrastructure.Persistence.Seeding;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.IntegrationTests.Persistence;

public sealed class SeedingTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    [Fact]
    public async Task Reference_data_is_seeded()
    {
        var (roles, branches) = await Host.QueryAsync(async db => (
            await db.Roles.Include(r => r.Permissions).ToListAsync(Ct),
            await db.Branches.CountAsync(Ct)));

        Assert.Equal(7, roles.Count);
        Assert.Equal(6, branches);

        foreach (var seed in RoleSeedCatalog.All)
        {
            var role = roles.Single(r => r.Code == seed.Code);
            Assert.Equal(seed.Permissions.Order(), role.Permissions.Select(p => p.Permission).Order());
            Assert.Equal(seed.DataScope, role.DataScope);
            Assert.Equal(seed.Kind, role.Kind);
            Assert.True(role.IsSystem);
        }
    }

    [Fact]
    public async Task Seeding_is_idempotent_and_keeps_administrator_changes()
    {
        // An administrator removes a permission; a restart must not put it back.
        await Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlow.Infrastructure.Persistence.CreditFlowDbContext>();
            var uw = await db.Roles.SingleAsync(r => r.Code == RoleCodes.Underwriter, Ct);
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(
                p => p.RoleId == uw.Id && p.Permission == "reports.view", Ct));
            await db.SaveChangesAsync(Ct);
        });

        await Host.Services.InitializeDatabaseAsync(Ct);
        await Host.Services.InitializeDatabaseAsync(Ct);

        var (roles, branches, users, uwPermissions) = await Host.QueryAsync(async db => (
            await db.Roles.CountAsync(Ct),
            await db.Branches.CountAsync(Ct),
            await db.Users.CountAsync(Ct),
            await db.RolePermissions.CountAsync(p => p.Permission == "reports.view", Ct)));

        Assert.Equal(7, roles);
        Assert.Equal(6, branches);
        Assert.Equal(1, users);
        Assert.Equal(4, uwPermissions); // OP, SUW, CC and AUD; the Underwriter lost it
    }

    [Fact]
    public async Task First_administrator_is_seeded_and_must_change_password()
    {
        await Host.InScopeAsync(async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await userManager.FindByNameAsync(InfrastructureHost.AdminUserName);

            Assert.NotNull(admin);
            Assert.True(admin.MustChangePassword);
            Assert.True(await userManager.CheckPasswordAsync(admin, InfrastructureHost.AdminPassword));

            var db = sp.GetRequiredService<CreditFlow.Infrastructure.Persistence.CreditFlowDbContext>();
            var row = await db.UserDirectory.SingleAsync(u => u.Id == admin.Id, Ct);
            Assert.Equal(RoleCodes.Admin, row.RoleCode);
            Assert.Null(row.BranchId);
            Assert.True(row.IsActive);
        });
    }

    [Fact]
    public async Task Demo_users_are_not_seeded_outside_development()
    {
        var users = await Host.QueryAsync(db => db.Users.CountAsync(Ct));

        Assert.Equal(1, users);
    }
}

public sealed class DevelopmentSeedingTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    protected override string Environment => "Development";

    protected override bool SeedDevUsers => true;

    [Fact]
    public async Task Demo_personas_cover_every_role_with_enough_people_for_four_eyes()
    {
        var rows = await Host.QueryAsync(db => db.UserDirectory.ToListAsync(Ct));
        var personas = rows.Where(r => r.UserName != InfrastructureHost.AdminUserName).ToList();

        Assert.Equal(12, personas.Count);
        Assert.Equal(3, personas.Count(r => r.RoleCode == RoleCodes.Operator));
        Assert.Equal(2, personas.Count(r => r.RoleCode == RoleCodes.Underwriter));
        Assert.Equal(2, personas.Count(r => r.RoleCode == RoleCodes.SeniorUnderwriter));
        Assert.Equal(3, personas.Count(r => r.RoleCode == RoleCodes.CreditCommittee));
        Assert.Equal(1, personas.Count(r => r.RoleCode == RoleCodes.Compliance));
        Assert.Equal(1, personas.Count(r => r.RoleCode == RoleCodes.Auditor));

        // Operators work in two branches; nobody else has a branch.
        Assert.Equal(
            ["Batumi", "Vake", "Vake"],
            personas.Where(r => r.RoleCode == RoleCodes.Operator).Select(r => r.BranchName).Order());
        Assert.All(personas.Where(r => r.RoleCode != RoleCodes.Operator), r => Assert.Null(r.BranchName));
        Assert.All(personas, r => Assert.False(r.MustChangePassword));
    }
}