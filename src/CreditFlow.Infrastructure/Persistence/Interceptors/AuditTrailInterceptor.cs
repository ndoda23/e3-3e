using System.Text.Json;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CreditFlow.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps <see cref="IAuditedEntity"/> (created/updated by and at) and appends an <see cref="AuditLog"/> record with
/// before/after values for every change to audited entities, in the same SaveChanges. Secrets are left out, and
/// personal data is masked with <see cref="SensitiveData"/> (personal numbers keep their last four digits).
/// Authentication accounts are not audited here (sign-in bookkeeping would flood the log); security events are
/// written explicitly instead.
/// </summary>
public sealed class AuditTrailInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    /// <summary>Audited entity types and the entity type name stored in the log.</summary>
    private static readonly Dictionary<Type, string> AuditedTypes = new()
    {
        [typeof(StaffMember)] = "User",
        [typeof(ApplicationRole)] = "Role",
        [typeof(Branch)] = "Branch",
    };

    /// <summary>Properties never written to the log.</summary>
    private static readonly HashSet<string> Redacted =
    [
        "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "AuthenticatorKey", "RecoveryCodes",
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            Apply(context);
        }

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            Apply(context);
        }

        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext context)
    {
        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();
        var actor = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditedEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.MarkCreated(actor, now);
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.MarkUpdated(actor, now);
            }
        }

        var records = new List<AuditLog>();
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (!AuditedTypes.TryGetValue(entry.Metadata.ClrType, out var entityType)
                || entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var (before, after) = Describe(entry);
            if (entry.State == EntityState.Modified && after.Count == 0)
            {
                continue;
            }

            records.Add(AuditLog.Create(new AuditEntry
            {
                OccurredAt = now,
                Action = entry.State switch
                {
                    EntityState.Added => AuditActions.Created,
                    EntityState.Deleted => AuditActions.Deleted,
                    _ => AuditActions.Updated,
                },
                UserId = actor?.Value,
                UserName = currentUser.UserName,
                EntityType = entityType,
                EntityId = KeyOf(entry),
                BeforeJson = before.Count == 0 ? null : SensitiveData.MaskText(JsonSerializer.Serialize(before)),
                AfterJson = after.Count == 0 ? null : SensitiveData.MaskText(JsonSerializer.Serialize(after)),
                IpAddress = currentUser.IpAddress,
                UserAgent = currentUser.UserAgent,
                CorrelationId = currentUser.CorrelationId,
            }));
        }

        context.AddRange(records);
    }

    private static (Dictionary<string, object?> Before, Dictionary<string, object?> After) Describe(EntityEntry entry)
    {
        var before = new Dictionary<string, object?>(StringComparer.Ordinal);
        var after = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (Redacted.Contains(name))
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    after[name] = Logged(property, property.CurrentValue);
                    break;
                case EntityState.Deleted:
                    before[name] = Logged(property, property.OriginalValue);
                    break;
                case EntityState.Modified when property.IsModified && !Equals(property.OriginalValue, property.CurrentValue):
                    before[name] = Logged(property, property.OriginalValue);
                    after[name] = Logged(property, property.CurrentValue);
                    break;
            }
        }

        return (before, after);
    }

    /// <summary>The value as written to the log: provider value, masked when the field is sensitive.</summary>
    private static object? Logged(PropertyEntry property, object? value)
    {
        var name = property.Metadata.Name;
        var provider = ToProvider(property, value);
        return SensitiveData.IsSensitiveField(name) ? SensitiveData.MaskField(name, provider?.ToString()) : provider;
    }

    private static object? ToProvider(PropertyEntry property, object? value) =>
        value is null ? null : property.Metadata.GetValueConverter()?.ConvertToProvider(value) ?? value;

    private static string KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey()!;
        return string.Join(",", key.Properties.Select(p => ToProvider(entry.Property(p.Name), entry.Property(p.Name).CurrentValue)));
    }
}
