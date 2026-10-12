using CreditFlow.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CreditFlow.Infrastructure.Persistence.Configurations;

internal sealed class LogEntryConfiguration : IEntityTypeConfiguration<LogEntry>
{
    public void Configure(EntityTypeBuilder<LogEntry> builder)
    {
        builder.ToTable(LogEntry.TableName);
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).UseIdentityColumn();
        builder.Property(l => l.Level).HasMaxLength(LogEntry.LevelMaxLength).IsRequired();
        builder.Property(l => l.UserName).HasMaxLength(LogEntry.UserNameMaxLength);
        builder.Property(l => l.RequestPath).HasMaxLength(LogEntry.RequestPathMaxLength);
        builder.Property(l => l.CorrelationId).HasMaxLength(LogEntry.CorrelationIdMaxLength);
        builder.HasIndex(l => l.TimeStamp);
    }
}
