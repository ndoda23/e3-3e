using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence;

/// <summary>The single CreditFlow database context: Identity, staff, reference data, audit log and Data Protection keys.</summary>
public sealed class CreditFlowDbContext(DbContextOptions<CreditFlowDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserPasswordHistory> PasswordHistory => Set<UserPasswordHistory>();

    public DbSet<UserDirectoryRow> UserDirectory => Set<UserDirectoryRow>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<SeedHistoryEntry> SeedHistory => Set<SeedHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(CreditFlowDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<UserId>().HaveConversion<UserIdConverter>();
        configurationBuilder.Properties<RoleId>().HaveConversion<RoleIdConverter>();
        configurationBuilder.Properties<BranchId>().HaveConversion<BranchIdConverter>();
        configurationBuilder.Properties<AuditLogId>().HaveConversion<AuditLogIdConverter>();

        // Enums are stored as readable strings.
        configurationBuilder.Properties<DataScope>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<DashboardKind>().HaveConversion<string>().HaveMaxLength(30);
        configurationBuilder.Properties<AttributeRule>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<RoleKind>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<Currency>().HaveConversion<string>().HaveMaxLength(3);
    }
}
