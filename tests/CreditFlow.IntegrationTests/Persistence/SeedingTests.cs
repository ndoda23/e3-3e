using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Users;
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
        var (roles, branches, limits) = await Host.QueryAsync(async db => (
            await db.Roles.Include(r => r.Permissions).ToListAsync(Ct),
            await db.Branches.CountAsync(Ct),
            await db.AuthorityLimits.ToListAsync(Ct)));

        Assert.Equal(10, roles.Count);
        Assert.Equal(6, branches);
        Assert.Equal(4, limits.Count(l => l.LimitType == LimitType.CreditDecision));
        Assert.Equal(2, limits.Count(l => l.LimitType == LimitType.PricingException));

        foreach (var seed in RoleSeedCatalog.All)
        {
            var role = roles.Single(r => r.Code == seed.Code);
            Assert.Equal(seed.Permissions.Order(), role.Permissions.Select(p => p.Permission).Order());
            Assert.Equal(seed.DataScope, role.DataScope);
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

        var (roles, branches, limits, users, uwPermissions) = await Host.QueryAsync(async db => (
            await db.Roles.CountAsync(Ct),
            await db.Branches.CountAsync(Ct),
            await db.AuthorityLimits.CountAsync(Ct),
            await db.Users.CountAsync(Ct),
            await db.RolePermissions.CountAsync(p => p.Permission == "reports.view", Ct)));

        Assert.Equal(10, roles);
        Assert.Equal(6, branches);
        Assert.Equal(6, limits);
        Assert.Equal(1, users);
        Assert.Equal(8, uwPermissions); // 9 business roles minus UW
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
            Assert.Equal(RoleCodes.SystemAdministrator, row.RoleCode);
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
    public async Task Demo_personas_are_seeded_in_development_with_their_roles_and_attributes()
    {
        var rows = await Host.QueryAsync(db => db.UserDirectory.ToListAsync(Ct));

        Assert.Equal(15, rows.Count);
        AssertPersona(rows, "tamta.tatishvili", RoleCodes.Underwriter, "Tbilisi HQ", null, AuthorityLevel.Senior);
        AssertPersona(rows, "anna.gelashvili", RoleCodes.LoanOfficer, "Batumi", null, AuthorityLevel.Junior);
        AssertPersona(rows, "mariam.lomidze", RoleCodes.BranchManager, "Tbilisi HQ", null, null);
        AssertPersona(rows, "sophio.gogichaishvili", RoleCodes.Head, null, Segment.Retail, null);
        AssertPersona(rows, "irakli.tsereteli", RoleCodes.Head, null, Segment.Business, null);
        AssertPersona(rows, "zurab.kapanadze", RoleCodes.Director, null, null, null);
        Assert.All(rows.Where(r => r.UserName != InfrastructureHost.AdminUserName), r => Assert.False(r.MustChangePassword));
    }

    private static void AssertPersona(
        List<CreditFlow.Infrastructure.Persistence.UserDirectoryRow> rows,
        string userName,
        string roleCode,
        string? branch,
        Segment? segment,
        AuthorityLevel? level)
    {
        var row = rows.Single(r => r.UserName == userName);
        Assert.Equal(roleCode, row.RoleCode);
        Assert.Equal(branch, row.BranchName);
        Assert.Equal(segment, row.Segment);
        Assert.Equal(level, row.AuthorityLevel);
    }
}
