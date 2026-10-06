using Lams.Application.Common;
using Lams.Application.ReferenceData;
using Lams.Application.Users;
using Lams.Domain.Branches;
using Lams.Domain.Common;
using Lams.Domain.Roles;
using Lams.Domain.Users;
using Lams.Infrastructure.Persistence;
using Lams.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lams.IntegrationTests.Persistence;

/// <summary>
/// The split user model: account (AspNetUsers) and staff member (StaffMembers) share one id, are written in one
/// transaction, and AspNetUserRoles always mirrors StaffMember.RoleId with exactly one row.
/// </summary>
public sealed class UserModelPersistenceTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    [Fact]
    public async Task Creating_a_user_writes_account_staff_member_and_one_user_role()
    {
        var issued = await CreateSeniorUnderwriterInVakeAsync();

        var (account, staff, userRoles, uwRoleId) = await Host.QueryAsync(async db => (
            await db.Users.SingleOrDefaultAsync(u => u.Id == issued.UserId.Value, Ct),
            await db.StaffMembers.SingleOrDefaultAsync(s => s.Id == issued.UserId, Ct),
            await db.UserRoles.Where(ur => ur.UserId == issued.UserId.Value).ToListAsync(Ct),
            await db.Roles.Where(r => r.Code == RoleCodes.Underwriter).Select(r => r.Id).SingleAsync(Ct)));

        Assert.NotNull(account);
        Assert.True(account.MustChangePassword);
        Assert.NotNull(account.TemporaryPasswordExpiresAt);
        Assert.NotNull(staff);
        Assert.Equal(new RoleId(uwRoleId), staff.RoleId);
        Assert.Equal(AuthorityLevel.Senior, staff.AuthorityLevel);
        var userRole = Assert.Single(userRoles);
        Assert.Equal(uwRoleId, userRole.RoleId);
    }

    [Fact]
    public async Task Created_and_updated_stamps_are_set_from_the_acting_user()
    {
        var admin = await AdminIdAsync();
        Host.CurrentUser.UserId = admin;

        var issued = await CreateSeniorUnderwriterInVakeAsync();

        var staff = await Host.QueryAsync(db => db.StaffMembers.SingleAsync(s => s.Id == issued.UserId, Ct));
        Assert.Equal(admin, staff.CreatedBy);
        Assert.NotEqual(default, staff.CreatedAt);
        Assert.Null(staff.UpdatedAt);
    }

    [Fact]
    public async Task A_failure_after_the_account_was_created_rolls_back_everything()
    {
        var id = UserId.New();

        var result = await Host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var identity = sp.GetRequiredService<IIdentityService>();

            return await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    var created = await identity.CreateAccountAsync(
                        new NewAccount(id, "rollback.user", "rollback@pcb.ge", "Temp!Passw0rd-123", DateTimeOffset.UtcNow.AddDays(1), false),
                        token);
                    Assert.True(created.IsSuccess);

                    return Result.Failure(Error.Conflict("Test.Failure", "Simulated failure after the account was written."));
                },
                Ct);
        });

        Assert.True(result.IsFailure);
        Assert.False(await Host.QueryAsync(db => db.Users.AnyAsync(u => u.Id == id.Value, Ct)));
    }

    [Fact]
    public async Task A_database_error_while_saving_the_staff_member_rolls_back_the_account()
    {
        var id = UserId.New();

        await Assert.ThrowsAsync<DbUpdateException>(() => Host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var identity = sp.GetRequiredService<IIdentityService>();
            var db = sp.GetRequiredService<LamsDbContext>();

            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    await identity.CreateAccountAsync(
                        new NewAccount(id, "broken.user", "broken@pcb.ge", "Temp!Passw0rd-123", DateTimeOffset.UtcNow.AddDays(1), false),
                        token);

                    // A role that does not exist violates FK_StaffMembers_AspNetRoles_RoleId.
                    var ghostRole = new RoleProfile
                    {
                        Id = RoleId.New(),
                        Code = RoleCode.Create("GHOST").Value,
                        DataScope = DataScope.Organization,
                        Dashboard = DashboardKind.Fab,
                        BranchRule = AttributeRule.Optional,
                        SegmentRule = AttributeRule.NotAllowed,
                        AuthorityLevelRule = AttributeRule.NotAllowed,
                        IsAdministrative = false,
                    };
                    db.StaffMembers.Add(StaffMember.Create(id, "Broken User", ghostRole, null, null, null).Value);
                    await db.SaveChangesAsync(token);
                    return Result.Success();
                },
                Ct);
        }));

        Assert.False(await Host.QueryAsync(db => db.Users.AnyAsync(u => u.Id == id.Value, Ct)));
        Assert.False(await Host.QueryAsync(db => db.StaffMembers.AnyAsync(s => s.Id == id, Ct)));
    }

    [Fact]
    public async Task A_staff_member_cannot_exist_without_an_account()
    {
        var role = await Host.QueryAsync(db => db.Roles.SingleAsync(r => r.Code == RoleCodes.Director, Ct));

        await Assert.ThrowsAsync<DbUpdateException>(() => Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<LamsDbContext>();
            db.StaffMembers.Add(StaffMember.Create(UserId.New(), "Orphan", role.ToProfile(), null, null, null).Value);
            await db.SaveChangesAsync(Ct);
        }));
    }

    [Fact]
    public async Task Changing_the_role_leaves_exactly_one_matching_user_role_row()
    {
        Host.CurrentUser.UserId = await AdminIdAsync();
        var issued = await CreateSeniorUnderwriterInVakeAsync();
        var bmRoleId = await Host.QueryAsync(db => db.Roles.Where(r => r.Code == RoleCodes.BranchManager).Select(r => r.Id).SingleAsync(Ct));

        var result = await Host.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<UserAdministrationService>();
            var details = (await service.GetDetailsAsync(issued.UserId, Ct))!;
            return await service.UpdateAsync(
                new UpdateUserCommand
                {
                    Id = issued.UserId,
                    UserName = details.UserName,
                    Email = details.Email,
                    FullName = details.FullName,
                    RoleId = new RoleId(bmRoleId),
                    BranchId = details.BranchId,
                },
                Ct);
        });

        Assert.True(result.IsSuccess, result.Error?.Description);
        var userRoles = await Host.QueryAsync(db => db.UserRoles.Where(ur => ur.UserId == issued.UserId.Value).ToListAsync(Ct));
        Assert.Equal(bmRoleId, Assert.Single(userRoles).RoleId);
    }

    [Fact]
    public async Task Identity_role_membership_reflects_the_staff_role()
    {
        var issued = await CreateSeniorUnderwriterInVakeAsync();

        await Host.InScopeAsync(async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<Lams.Infrastructure.Identity.ApplicationUser>>();
            var user = await userManager.FindByIdAsync(issued.UserId.ToString());
            Assert.Equal([RoleCodes.Underwriter], await userManager.GetRolesAsync(user!));
        });
    }

    private async Task<UserId> AdminIdAsync()
    {
        var id = await Host.QueryAsync(db =>
            db.Users.Where(u => u.UserName == InfrastructureHost.AdminUserName).Select(u => u.Id).SingleAsync(Ct));
        return new UserId(id);
    }

    private Task<IssuedTemporaryPassword> CreateSeniorUnderwriterInVakeAsync() =>
        Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<LamsDbContext>();
            var uw = await db.Roles.SingleAsync(r => r.Code == RoleCodes.Underwriter, Ct);
            var vake = await db.Branches.SingleAsync(b => b.Code == BranchSeedCatalog.Vake, Ct);

            var result = await sp.GetRequiredService<UserAdministrationService>().CreateAsync(
                new CreateUserCommand
                {
                    UserName = $"uw.{Guid.NewGuid():N}"[..20],
                    Email = $"{Guid.NewGuid():N}@pcb.ge",
                    FullName = "Tamta Tatishvili",
                    RoleId = new RoleId(uw.Id),
                    BranchId = vake.Id,
                    AuthorityLevel = AuthorityLevel.Senior,
                },
                Ct);

            Assert.True(result.IsSuccess, result.Error?.Description);
            return result.Value;
        });
}
