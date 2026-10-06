using Lams.Domain.Branches;
using Lams.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lams.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>StaffMembers</c>: the business user. Its primary key is also a required foreign key to <c>AspNetUsers.Id</c>
/// (1:1, shared key) and <c>RoleId</c> references <c>AspNetRoles.Id</c>. EF Core cannot model a foreign key between
/// properties of different CLR types (<c>UserId</c>/<c>RoleId</c> vs <c>Guid</c>), so those two constraints are
/// created explicitly in the migration (see <c>ManualForeignKeys</c>); the database enforces them.
/// </summary>
internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("StaffMembers");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.FullName).HasMaxLength(StaffMember.FullNameMaxLength).IsRequired();
        builder.Property(s => s.RoleId).IsRequired();
        builder.HasIndex(s => s.RoleId);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(s => s.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(s => s.Access);
    }
}
