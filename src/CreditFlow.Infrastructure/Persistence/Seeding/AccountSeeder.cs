using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CreditFlow.Infrastructure.Persistence.Seeding;

/// <summary>Creates the first System Administrator (all environments) and the demo users (Development only).</summary>
internal sealed partial class AccountSeeder(
    CreditFlowDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<SeedOptions> options,
    ILogger<AccountSeeder> logger)
{
    public async Task SeedAdministratorAsync(CancellationToken cancellationToken)
    {
        var admin = options.Value.Admin;
        if (string.IsNullOrWhiteSpace(admin.UserName))
        {
            if (!await db.UserDirectory.AnyAsync(u => u.IsActive && u.RoleIsAdministrative, cancellationToken))
            {
                LogNoAdministrator();
            }

            return;
        }

        if (await userManager.FindByNameAsync(admin.UserName) is not null)
        {
            return;
        }

        await CreateAsync(
            admin.UserName, admin.Email!, admin.FullName, RoleCodes.Admin, null,
            admin.Password!, mustChangePassword: true, cancellationToken);
        LogAdministratorCreated(admin.UserName);
    }

    public async Task SeedDevelopmentUsersAsync(CancellationToken cancellationToken)
    {
        var password = options.Value.DevUsers.Password!;
        foreach (var persona in DevPersonas.All)
        {
            if (await userManager.FindByNameAsync(persona.UserName) is not null)
            {
                continue;
            }

            BranchId? branchId = null;
            if (persona.BranchCode is { } code)
            {
                branchId = await db.Branches.Where(b => b.Code == code).Select(b => b.Id).SingleAsync(cancellationToken);
            }

            await CreateAsync(
                persona.UserName, persona.Email, persona.FullName, persona.RoleCode, branchId, password,
                mustChangePassword: false, cancellationToken);
        }
    }

    private async Task CreateAsync(
        string userName,
        string email,
        string fullName,
        string roleCode,
        BranchId? branchId,
        string password,
        bool mustChangePassword,
        CancellationToken cancellationToken)
    {
        var role = await db.Roles.AsNoTracking().SingleAsync(r => r.Code == roleCode, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var user = new ApplicationUser
        {
            Id = UserId.New().Value,
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            MustChangePassword = mustChangePassword,
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Seeding account '{userName}' failed: {string.Join("; ", created.Errors.Select(e => e.Description))}");
        }

        var staff = StaffMember.Create(new UserId(user.Id), fullName, role.ToProfile(), branchId);
        if (staff.IsFailure)
        {
            throw new InvalidOperationException($"Seeding staff member '{userName}' failed: {staff.Error!.Description}");
        }

        db.StaffMembers.Add(staff.Value);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No active System Administrator exists and Seed:Admin is not configured. Nobody can administer CreditFlow.")]
    private partial void LogNoAdministrator();

    [LoggerMessage(Level = LogLevel.Information, Message = "Created System Administrator account {UserName}")]
    private partial void LogAdministratorCreated(string userName);
}
