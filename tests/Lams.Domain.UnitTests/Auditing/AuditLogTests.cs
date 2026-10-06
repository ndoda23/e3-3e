using System.Reflection;
using Lams.Domain.Auditing;

namespace Lams.Domain.UnitTests.Auditing;

public sealed class AuditLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_copies_the_entry()
    {
        var userId = Guid.NewGuid();

        var log = AuditLog.Create(new AuditEntry
        {
            OccurredAt = Now,
            Action = AuditActions.LoginSucceeded,
            UserId = userId,
            UserName = "tamta.tatishvili",
            IpAddress = "10.0.0.1",
        });

        Assert.Equal(Now, log.OccurredAt);
        Assert.Equal(AuditActions.LoginSucceeded, log.Action);
        Assert.Equal(userId, log.UserId);
        Assert.Equal("tamta.tatishvili", log.UserName);
        Assert.Equal("10.0.0.1", log.IpAddress);
    }

    [Fact]
    public void Action_is_required()
    {
        Assert.ThrowsAny<ArgumentException>(() => AuditLog.Create(new AuditEntry { OccurredAt = Now, Action = " " }));
    }

    [Fact]
    public void Long_context_is_truncated_rather_than_rejected()
    {
        var log = AuditLog.Create(new AuditEntry
        {
            OccurredAt = Now,
            Action = AuditActions.LoginFailed,
            UserAgent = new string('a', AuditLog.UserAgentMaxLength + 50),
        });

        Assert.Equal(AuditLog.UserAgentMaxLength, log.UserAgent!.Length);
    }

    [Fact]
    public void Audit_records_are_append_only_no_public_setters_or_mutators()
    {
        var type = typeof(AuditLog);

        var publicSetters = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name);

        var publicMutators = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name);

        Assert.Empty(publicSetters);
        Assert.Empty(publicMutators);
    }
}
