using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Infrastructure.ExternalServices;
using CreditFlow.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CreditFlow.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(OutboxMessage.TypeMaxLength);
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.ErrorMaxLength);

        // The dispatcher's claim query: unprocessed messages that are due.
        builder.HasIndex(m => new { m.AvailableAt, m.OccurredAt }).HasFilter("[ProcessedAt] IS NULL")
            .HasDatabaseName("IX_OutboxMessages_Due");
    }
}

internal sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("ProcessedMessages");
        builder.HasKey(p => new { p.MessageId, p.Handler });
        builder.Property(p => p.Handler).HasMaxLength(ProcessedMessage.HandlerMaxLength);
    }
}

internal sealed class SanctionsListEntryConfiguration : IEntityTypeConfiguration<SanctionsListEntry>
{
    public void Configure(EntityTypeBuilder<SanctionsListEntry> builder)
    {
        builder.ToTable("SanctionsListEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.PersonalNumber).HasMaxLength(PersonalNumber.Length).IsFixedLength().IsUnicode(false);
        builder.Property(e => e.FullName).HasMaxLength(200);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ListName).HasMaxLength(100);
        builder.HasIndex(e => e.PersonalNumber);
        builder.HasIndex(e => e.FullName);
    }
}

internal sealed class BureauReportRecordConfiguration : IEntityTypeConfiguration<BureauReportRecord>
{
    public void Configure(EntityTypeBuilder<BureauReportRecord> builder)
    {
        builder.ToTable("BureauReports");
        builder.HasKey(r => new { r.ApplicationId, r.ClientId });
        builder.HasOne<LoanApplication>().WithMany().HasForeignKey(r => r.ApplicationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class NotificationRecordConfiguration : IEntityTypeConfiguration<NotificationRecord>
{
    public void Configure(EntityTypeBuilder<NotificationRecord> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).UseIdentityColumn();
        builder.Property(n => n.Channel).HasConversion<string>().HasMaxLength(10);
        builder.Property(n => n.Recipient).HasMaxLength(NotificationRecord.RecipientMaxLength);
        builder.Property(n => n.Subject).HasMaxLength(NotificationRecord.SubjectMaxLength);
        builder.Property(n => n.Body).HasMaxLength(NotificationRecord.BodyMaxLength);
        builder.HasIndex(n => new { n.ApplicationId, n.CreatedAt });
    }
}
