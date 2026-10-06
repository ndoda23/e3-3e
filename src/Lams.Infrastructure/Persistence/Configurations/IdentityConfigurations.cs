using Lams.Domain.Roles;
using Lams.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lams.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.Property(r => r.Code).HasMaxLength(RoleCode.MaxLength).IsRequired();
        builder.HasIndex(r => r.Code).IsUnique();
        builder.Property(r => r.DisplayName).HasMaxLength(ApplicationRole.DisplayNameMaxLength).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(ApplicationRole.DescriptionMaxLength).IsRequired();

        builder.HasMany(r => r.Permissions)
            .WithOne()
            .HasForeignKey(p => p.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(p => new { p.RoleId, p.Permission });
        builder.Property(p => p.Permission).HasMaxLength(PermissionCode.MaxLength);
    }
}

/// <summary>AspNetUserRoles mirrors <c>StaffMember.RoleId</c>: at most one row per user.</summary>
internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder) =>
        builder.HasIndex(ur => ur.UserId).IsUnique().HasDatabaseName("UX_AspNetUserRoles_UserId");
}

internal sealed class UserPasswordHistoryConfiguration : IEntityTypeConfiguration<UserPasswordHistory>
{
    public void Configure(EntityTypeBuilder<UserPasswordHistory> builder)
    {
        builder.ToTable("UserPasswordHistory");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.PasswordHash).IsRequired();
        builder.HasIndex(h => new { h.UserId, h.CreatedAt });
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserDirectoryRowConfiguration : IEntityTypeConfiguration<UserDirectoryRow>
{
    public void Configure(EntityTypeBuilder<UserDirectoryRow> builder)
    {
        builder.HasNoKey();
        builder.ToView(UserDirectoryRow.ViewName);
    }
}
