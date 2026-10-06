using Lams.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lams.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>AuditLogs</c>: append-only. Besides the application guard (updates/deletes throw before reaching the database),
/// the migration creates an INSTEAD OF UPDATE, DELETE trigger that rejects any change made directly in SQL.
/// </summary>
internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", t => t.HasTrigger(AuditLogTrigger.Name));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        builder.Property(a => a.Action).HasMaxLength(AuditLog.ActionMaxLength).IsRequired();
        builder.Property(a => a.UserName).HasMaxLength(AuditLog.UserNameMaxLength);
        builder.Property(a => a.EntityType).HasMaxLength(AuditLog.EntityTypeMaxLength);
        builder.Property(a => a.EntityId).HasMaxLength(AuditLog.EntityIdMaxLength);
        builder.Property(a => a.IpAddress).HasMaxLength(AuditLog.IpAddressMaxLength);
        builder.Property(a => a.UserAgent).HasMaxLength(AuditLog.UserAgentMaxLength);
        builder.Property(a => a.CorrelationId).HasMaxLength(AuditLog.CorrelationIdMaxLength);

        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.Action);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}

internal static class AuditLogTrigger
{
    public const string Name = "TR_AuditLogs_AppendOnly";

    public const string CreateSql = $"""
        CREATE TRIGGER [dbo].[{Name}] ON [dbo].[AuditLogs]
        INSTEAD OF UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51000, 'AuditLogs is append-only: records cannot be updated or deleted.', 1;
        END
        """;

    public const string DropSql = $"DROP TRIGGER IF EXISTS [dbo].[{Name}];";
}
