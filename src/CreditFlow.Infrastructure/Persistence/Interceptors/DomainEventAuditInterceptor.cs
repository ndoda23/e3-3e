using System.Text.Json;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CreditFlow.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Audits the business events of applications in the same SaveChanges as the change: every status transition, every
/// scoring run with all rule results, every decision (approve, reject, counter-offer, vote, compliance) and every
/// override with its reason, and escalations. Runs before the outbox interceptor, which then clears the events.
/// </summary>
public sealed class DomainEventAuditInterceptor(ICurrentUser currentUser) : SaveChangesInterceptor
{
    private const string ApplicationEntity = "Application";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Audit(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Audit(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Audit(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var records = context.ChangeTracker.Entries<IHasDomainEvents>()
            .SelectMany(e => e.Entity.DomainEvents)
            .Select(ToEntry)
            .OfType<AuditEntry>()
            .Select(AuditLog.Create)
            .ToList();
        context.AddRange(records);
    }

    private AuditEntry? ToEntry(IDomainEvent domainEvent) => domainEvent switch
    {
        ApplicationStatusChanged e => Entry(
            AuditActions.ApplicationStatusChanged, e.ApplicationId, e.OccurredAt, e.By?.Value,
            e.From is null ? null : new { Status = e.From.ToString() },
            new { Status = e.To.ToString(), e.Reason }),
        ApplicationScored e => Entry(
            AuditActions.ApplicationScored, e.ApplicationId, e.OccurredAt, null, null,
            new
            {
                e.Run.RuleSetVersion,
                Results = e.Run.Results.Select(r => new { r.RuleCode, r.Passed, r.Overridable, r.Measured, r.Threshold, r.Reason }),
            }),
        ApplicationDecisionRecorded e => Entry(
            e.OverrideReason is null ? AuditActions.ApplicationDecision : AuditActions.ApplicationOverride,
            e.ApplicationId, e.OccurredAt, e.DecidedBy.Value, null,
            new { Kind = e.Kind.ToString(), e.Reason, e.OverrideReason }),
        ApplicationEscalated e => Entry(AuditActions.ApplicationEscalated, e.ApplicationId, e.OccurredAt, null, null, new { }),
        _ => null,
    };

    private AuditEntry Entry(
        string action, LoanApplicationId applicationId, DateTimeOffset at, Guid? userId, object? before, object after) =>
        new()
        {
            OccurredAt = at,
            Action = action,
            UserId = userId ?? currentUser.UserId?.Value,
            UserName = userId is null || userId == currentUser.UserId?.Value ? currentUser.UserName : null,
            EntityType = ApplicationEntity,
            EntityId = applicationId.ToString(),
            BeforeJson = before is null ? null : SensitiveData.MaskText(JsonSerializer.Serialize(before)),
            AfterJson = SensitiveData.MaskText(JsonSerializer.Serialize(after)),
            IpAddress = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent,
            CorrelationId = currentUser.CorrelationId,
        };
}
