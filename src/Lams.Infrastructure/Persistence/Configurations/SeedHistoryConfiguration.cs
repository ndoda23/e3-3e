using Lams.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lams.Infrastructure.Persistence.Configurations;

internal sealed class SeedHistoryConfiguration : IEntityTypeConfiguration<SeedHistoryEntry>
{
    public void Configure(EntityTypeBuilder<SeedHistoryEntry> builder)
    {
        builder.ToTable("SeedHistory");
        builder.HasKey(s => s.SeedId);
        builder.Property(s => s.SeedId).HasMaxLength(SeedHistoryEntry.SeedIdMaxLength);
    }
}
