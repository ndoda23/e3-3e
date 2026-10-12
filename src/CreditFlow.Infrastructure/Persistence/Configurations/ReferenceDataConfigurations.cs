using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Branches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CreditFlow.Infrastructure.Persistence.Configurations;

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Code).HasMaxLength(Branch.CodeMaxLength).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(Branch.NameMaxLength).IsRequired();
        builder.Property(b => b.City).HasMaxLength(Branch.CityMaxLength).IsRequired();
        builder.HasIndex(b => b.Code).IsUnique();
    }
}

/// <summary><c>AuthorityLimits</c>. <c>RoleId</c> references <c>AspNetRoles.Id</c> (constraint created in the migration).</summary>
internal sealed class AuthorityLimitConfiguration : IEntityTypeConfiguration<AuthorityLimit>
{
    public void Configure(EntityTypeBuilder<AuthorityLimit> builder)
    {
        builder.ToTable("AuthorityLimits", t =>
        {
            t.HasCheckConstraint(
                "CK_AuthorityLimits_CreditDecision",
                "[LimitType] <> 'CreditDecision' OR ([Currency] IS NOT NULL AND [MaxAmount] > 0)");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.MaxAmount).HasPrecision(18, 2);
        builder.Property(l => l.MaxRateGapPp).HasPrecision(5, 2);
        builder.Property(l => l.MinCustomerRoaePct).HasPrecision(5, 2);

        // One row per role / type / level / currency. NULLs count as equal (no automatic filter on nullable columns).
        builder.HasIndex(l => new { l.RoleId, l.LimitType, l.AuthorityLevel, l.Currency })
            .IsUnique()
            .HasFilter(null);
    }
}
