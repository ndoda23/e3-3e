using System.Security.Claims;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Application.Roles;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.Infrastructure.Persistence;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.IntegrationTests.Persistence;

public sealed class AuditAndSecurityPersistenceTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    [Fact]
    public async Task Data_changes_are_written_to_the_audit_log_with_the_actor()
    {
        var admin = await AdminAsync();
        Host.CurrentUser.UserId = new UserId(admin.Id);
        Host.CurrentUser.UserName = admin.UserName;

        var created = await Host.InScopeAsync(sp => sp.GetRequiredService<CreditFlow.Application.Branches.BranchAdministrationService>()
            .CreateAsync(new CreditFlow.Application.Branches.CreateBranchCommand("GLD", "Gldani", "Tbilisi"), Ct));

        var record = await Host.QueryAsync(db => db.AuditLogs
            .SingleAsync(l => l.EntityType == "Branch" && l.EntityId == created.Value.ToString(), Ct));
        Assert.Equal(AuditActions.Created, record.Action);
        Assert.Equal(admin.Id, record.UserId);
        Assert.Equal("10.0.0.1", record.IpAddress);
        Assert.Contains("Gldani", record.AfterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audit_records_cannot_be_updated_through_the_application()
    {
        await WriteAuditRecordAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlowDbContext>();
            var record = await db.AuditLogs.FirstAsync(Ct);
            db.Entry(record).Property(r => r.Action).CurrentValue = "Tampered";
            await db.SaveChangesAsync(Ct);
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlowDbContext>();
            db.AuditLogs.Remove(await db.AuditLogs.FirstAsync(Ct));
            await db.SaveChangesAsync(Ct);
        }));
    }

    [Fact]
    public async Task Audit_records_cannot_be_updated_or_deleted_in_SQL()
    {
        await WriteAuditRecordAsync();

        var update = await Assert.ThrowsAsync<SqlException>(() => Host.QueryAsync(db =>
            db.Database.ExecuteSqlRawAsync("UPDATE [AuditLogs] SET [Action] = 'Tampered'", Ct)));
        Assert.Contains("append-only", update.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<SqlException>(() => Host.QueryAsync(db =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM [AuditLogs]", Ct)));
    }

    [Fact]
    public async Task The_last_five_passwords_cannot_be_reused()
    {
        var admin = await AdminAsync();
        var id = new UserId(admin.Id);
        string[] passwords = ["Second!Passw0rd-1", "Third!Passw0rd-22", "Fourth!Passw0rd-3", "Fifth!Passw0rd-44"];

        await Host.InScopeAsync(async sp =>
        {
            var identity = sp.GetRequiredService<IIdentityService>();
            var expires = DateTimeOffset.UtcNow.AddDays(1);
            foreach (var password in passwords)
            {
                Assert.True((await identity.SetTemporaryPasswordAsync(id, password, expires, Ct)).IsSuccess);
            }

            // Current + 4 previous = the last 5: the initial password and every one above are rejected.
            foreach (var reused in passwords.Prepend(InfrastructureHost.AdminPassword))
            {
                var result = await identity.SetTemporaryPasswordAsync(id, reused, expires, Ct);
                Assert.Equal("Identity.PasswordReused", Assert.Single(result.Error!.Details.DefaultIfEmpty(result.Error)).Code);
            }

            Assert.True((await identity.SetTemporaryPasswordAsync(id, "Brand!New-Passw0rd", expires, Ct)).IsSuccess);
        });
    }

    [Fact]
    public async Task The_sixth_oldest_password_may_be_used_again()
    {
        var id = new UserId((await AdminAsync()).Id);

        await Host.InScopeAsync(async sp =>
        {
            var identity = sp.GetRequiredService<IIdentityService>();
            var expires = DateTimeOffset.UtcNow.AddDays(1);
            foreach (var password in new[] { "One!Passw0rd-aaaa", "Two!Passw0rd-bbbb", "Three!Passw0rd-cc", "Four!Passw0rd-ddd", "Five!Passw0rd-eee" })
            {
                Assert.True((await identity.SetTemporaryPasswordAsync(id, password, expires, Ct)).IsSuccess);
            }

            // The seeded password is now six passwords back.
            Assert.True((await identity.SetTemporaryPasswordAsync(id, InfrastructureHost.AdminPassword, expires, Ct)).IsSuccess);
        });

        var history = await Host.QueryAsync(db => db.PasswordHistory.CountAsync(h => h.UserId == id.Value, Ct));
        Assert.Equal(PasswordHistoryValidator.StoredHistoryDepth, history);
    }

    [Fact]
    public async Task The_cookie_principal_carries_only_stable_identity_claims()
    {
        var admin = await AdminAsync();

        var principal = await Host.InScopeAsync(async sp =>
        {
            var factory = sp.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            return await factory.CreateAsync((await userManager.FindByIdAsync(admin.Id.ToString()))!);
        });

        var adminRole = await Host.QueryAsync(db => db.Roles.SingleAsync(r => r.Code == RoleCodes.SystemAdministrator, Ct));
        Assert.Equal(
            [ClaimTypes.NameIdentifier, ClaimTypes.Name, "AspNet.Identity.SecurityStamp", CreditFlowClaimTypes.RoleId],
            principal.Claims.Select(c => c.Type));
        Assert.Equal(adminRole.Id.ToString(), principal.FindFirstValue(CreditFlowClaimTypes.RoleId));
        Assert.DoesNotContain(principal.Claims, c => c.Type == ClaimTypes.Role);
    }

    [Fact]
    public async Task Access_profile_is_cached_and_reflects_changes_after_invalidation()
    {
        var admin = await AdminAsync();
        Host.CurrentUser.UserId = new UserId(admin.Id);
        var uw = await Host.QueryAsync(db => db.Roles.SingleAsync(r => r.Code == RoleCodes.Underwriter, Ct));
        var (vake, saburtalo) = await Host.QueryAsync(async db => (
            await db.Branches.SingleAsync(b => b.Code == BranchSeedCatalog.Vake, Ct),
            await db.Branches.SingleAsync(b => b.Code == BranchSeedCatalog.Saburtalo, Ct)));

        var issued = await Host.InScopeAsync(sp => sp.GetRequiredService<UserAdministrationService>().CreateAsync(
            new CreateUserCommand
            {
                UserName = "tamta.t", Email = "tamta.t@pcb.ge", FullName = "Tamta Tatishvili",
                RoleId = new RoleId(uw.Id), BranchId = vake.Id, AuthorityLevel = AuthorityLevel.Senior,
            },
            Ct));
        var userId = issued.Value.UserId;

        var before = await ProfileAsync(userId);
        Assert.True(before.HasPermission(Permissions.Reports.View));
        Assert.Equal(vake.Id, before.BranchId);
        Assert.Equal("Vake", before.BranchName);

        // Role permissions change → the next resolution sees it (no sign-out needed).
        await Host.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<RoleAdministrationService>();
            var details = (await service.ListAsync(Ct)).Single(r => r.Id == new RoleId(uw.Id));
            var result = await service.UpdatePermissionsAsync(
                new UpdateRolePermissionsCommand(details.Id, details.Permissions.Where(p => p != Permissions.Reports.View).ToList()), Ct);
            Assert.True(result.IsSuccess);
        });
        Assert.False((await ProfileAsync(userId)).HasPermission(Permissions.Reports.View));

        // Branch change → the cached staff snapshot is replaced.
        await Host.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<UserAdministrationService>();
            var d = (await service.GetDetailsAsync(userId, Ct))!;
            var result = await service.UpdateAsync(
                new UpdateUserCommand
                {
                    Id = userId, UserName = d.UserName, Email = d.Email, FullName = d.FullName, RoleId = d.RoleId,
                    BranchId = saburtalo.Id, AuthorityLevel = d.AuthorityLevel,
                },
                Ct);
            Assert.True(result.IsSuccess);
        });
        var after = await ProfileAsync(userId);
        Assert.Equal(saburtalo.Id, after.BranchId);
        Assert.Equal("Saburtalo", after.BranchName);
    }

    private Task<UserAccessProfile> ProfileAsync(UserId userId) =>
        Host.InScopeAsync(async sp => (await sp.GetRequiredService<IUserAccessProfileService>().GetAsync(userId, Ct))!);

    private Task<ApplicationUser> AdminAsync() =>
        Host.QueryAsync(db => db.Users.SingleAsync(u => u.UserName == InfrastructureHost.AdminUserName, Ct));

    private Task WriteAuditRecordAsync() =>
        Host.InScopeAsync(sp => sp.GetRequiredService<CreditFlow.Application.Auditing.IAuditWriter>()
            .WriteAsync(new CreditFlow.Application.Auditing.AuditEvent(AuditActions.LoginSucceeded), Ct));
}
