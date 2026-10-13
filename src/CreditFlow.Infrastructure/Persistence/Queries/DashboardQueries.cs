using CreditFlow.Application.Applications;
using CreditFlow.Application.Auditing;
using CreditFlow.Application.Dashboard;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Roles;
using CreditFlow.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreditFlow.Infrastructure.Persistence.Queries;

/// <summary>
/// Dashboard counts and lists. Every count is the total of the list its tile links to: one status, one
/// <see cref="ApplicationQueueKind"/> (<see cref="ApplicationScope.InQueue"/>), one user status
/// (<see cref="UserStatusFilters.WithStatus"/>) or one audit category (<see cref="AuditScope.InCategory"/>) — always
/// within the viewer's access filter. A list is one ordering over all its kinds, so no kind can crowd out another:
/// a work list oldest first, an event feed newest first.
/// </summary>
internal sealed class DashboardQueries(CreditFlowDbContext db, IOptions<DashboardOptions> options) : IDashboardQueries
{
    /// <summary>Statuses where the operator has the next step.</summary>
    private static readonly ApplicationStatus[] OperatorActionable =
    [
        ApplicationStatus.DocumentsPending, ApplicationStatus.Approved, ApplicationStatus.ConditionallyApproved, ApplicationStatus.ConditionsMet,
    ];

    private decimal LargeDisbursementGel => options.Value.LargeDisbursementGel;

    public async Task<DashboardData> OperatorAsync(ApplicationAccessFilter access, DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        var visible = Visible(access);
        var byStatus = await visible.GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
        int Of(ApplicationStatus status) => byStatus.GetValueOrDefault(status);

        DashboardTile[] tiles =
        [
            new(DashboardMetric.Drafts, Of(ApplicationStatus.Draft)),
            new(DashboardMetric.DocumentsPending, Of(ApplicationStatus.DocumentsPending)),
            new(DashboardMetric.AwaitingSignature, await CountAsync(access, ApplicationQueueKind.AwaitingSignature, now, cancellationToken)),
            new(DashboardMetric.AwaitingDisbursement, Of(ApplicationStatus.ConditionsMet)),
            new(DashboardMetric.PayoutsPending, await CountAsync(access, ApplicationQueueKind.PayoutPending, now, cancellationToken)),
            new(DashboardMetric.PayoutsFailed, await CountAsync(access, ApplicationQueueKind.PayoutFailed, now, cancellationToken), Alert: true),
            new(DashboardMetric.StuckInPreCheck, await CountAsync(access, ApplicationQueueKind.StuckInPreCheck, now, cancellationToken), Alert: true),
        ];

        // One work list, oldest waiting first; failed payouts and stuck pre-checks are marked, not moved ahead.
        var failed = visible.InQueue(ApplicationQueueKind.PayoutFailed, now, db, LargeDisbursementGel).Select(a => a.Id);
        var stuck = visible.InQueue(ApplicationQueueKind.StuckInPreCheck, now, db, LargeDisbursementGel).Select(a => a.Id);
        var rows = await (
                from a in visible
                join c in db.Clients on a.BorrowerId equals c.Id
                let payoutFailed = failed.Contains(a.Id)
                let stuckInPreCheck = stuck.Contains(a.Id)
                where payoutFailed || stuckInPreCheck || OperatorActionable.Contains(a.Status)
                orderby a.StatusChangedAt, a.Id
                select new { a.Id, a.Number, c.FirstName, c.LastName, a.Status, a.StatusChangedAt, a.AmountGel, payoutFailed, stuckInPreCheck })
            .Take(take)
            .ToListAsync(cancellationToken);

        var attention = rows.Select(r => new AttentionItem(
                AttentionKind.Application, r.Id.Value.ToString(), $"{LoanApplication.FormatReference(r.Number)} · {r.FirstName} {r.LastName}",
                r.Status, null, r.StatusChangedAt, r.AmountGel,
                r.payoutFailed ? DashboardMetric.PayoutsFailed : r.stuckInPreCheck ? DashboardMetric.StuckInPreCheck : null))
            .ToList();
        return new DashboardData(DashboardKind.Operator, tiles, attention);
    }

    public async Task<DashboardData> AuditAsync(ApplicationAccessFilter access, DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        var since = ApplicationQueues.RecentSince(now);
        var accessChanges = AccessChanges(since);
        DashboardTile[] tiles =
        [
            new(DashboardMetric.RecentOverrides, await CountAsync(access, ApplicationQueueKind.RecentOverrides, now, cancellationToken)),
            new(DashboardMetric.RecentRejections, await CountAsync(access, ApplicationQueueKind.RecentlyRejected, now, cancellationToken)),
            new(DashboardMetric.ComplianceClearances, await CountAsync(access, ApplicationQueueKind.RecentComplianceClearances, now, cancellationToken)),
            new(DashboardMetric.LargeDisbursements, await CountAsync(access, ApplicationQueueKind.LargeDisbursements, now, cancellationToken)),
            new(DashboardMetric.PayoutIncidents, await CountAsync(access, ApplicationQueueKind.PayoutIncidents, now, cancellationToken), Alert: true),
            new(DashboardMetric.RecentEscalations, await CountAsync(access, ApplicationQueueKind.RecentlyEscalated, now, cancellationToken)),
            new(DashboardMetric.RecentAccessChanges, await accessChanges.CountAsync(cancellationToken)),
        ];

        // Each kind newest first, at most `take` of it; then all kinds merged by time. The newest `take` events overall
        // are always among these, whatever the mix.
        var visible = Visible(access);
        var large = LargeDisbursementGel;
        List<FeedRow> events =
        [
            .. await (
                    from a in visible
                    from d in a.Decisions
                    where d.OverrideReason != null && d.DecidedAt >= since
                    join c in db.Clients on a.BorrowerId equals c.Id
                    orderby d.DecidedAt descending, d.Id
                    select new FeedRow(DashboardMetric.RecentOverrides, a.Id, a.Number, c.FirstName, c.LastName, a.Status, a.AmountGel,
                        d.DecidedAt, d.DecidedBy.Value, d.OverrideReason))
                .Take(take).ToListAsync(cancellationToken),
            .. await (
                    from a in visible
                    from h in a.History
                    where h.To == ApplicationStatus.Rejected && h.At >= since
                    join c in db.Clients on a.BorrowerId equals c.Id
                    orderby h.At descending, h.Id
                    select new FeedRow(DashboardMetric.RecentRejections, a.Id, a.Number, c.FirstName, c.LastName, a.Status, a.AmountGel,
                        h.At, h.By == null ? null : h.By.Value.Value, h.Reason))
                .Take(take).ToListAsync(cancellationToken),
            .. await (
                    from a in visible
                    from d in a.Decisions
                    where d.Kind == DecisionKind.ComplianceClear && d.DecidedAt >= since
                    join c in db.Clients on a.BorrowerId equals c.Id
                    orderby d.DecidedAt descending, d.Id
                    select new FeedRow(DashboardMetric.ComplianceClearances, a.Id, a.Number, c.FirstName, c.LastName, a.Status, a.AmountGel,
                        d.DecidedAt, d.DecidedBy.Value, d.Reason))
                .Take(take).ToListAsync(cancellationToken),
            .. await (
                    from d in db.Disbursements
                    join a in visible on d.ApplicationId equals a.Id
                    join c in db.Clients on a.BorrowerId equals c.Id
                    where d.Status == DisbursementStatus.Succeeded && d.CompletedAt >= since && a.AmountGel >= large
                    orderby d.CompletedAt descending, d.Id
                    select new FeedRow(DashboardMetric.LargeDisbursements, a.Id, a.Number, c.FirstName, c.LastName, a.Status, a.AmountGel,
                        d.CompletedAt!.Value, d.RequestedBy.Value, null))
                .Take(take).ToListAsync(cancellationToken),
            .. await (
                    from d in db.Disbursements
                    join a in visible on d.ApplicationId equals a.Id
                    join c in db.Clients on a.BorrowerId equals c.Id
                    where (d.Status == DisbursementStatus.Failed || d.Attempts > 1 || d.LastError != null)
                          && (d.CompletedAt ?? d.RequestedAt) >= since
                    orderby d.CompletedAt ?? d.RequestedAt descending, d.Id
                    select new FeedRow(DashboardMetric.PayoutIncidents, a.Id, a.Number, c.FirstName, c.LastName, a.Status, a.AmountGel,
                        d.CompletedAt ?? d.RequestedAt, d.RequestedBy.Value,
                        d.Status + ", attempt " + d.Attempts + (d.LastError == null ? string.Empty : ": " + d.LastError)))
                .Take(take).ToListAsync(cancellationToken),
            .. await (
                    from a in visible
                    where a.EscalatedAt >= since
                    join c in db.Clients on a.BorrowerId equals c.Id
                    orderby a.EscalatedAt descending, a.Id
                    select new FeedRow(DashboardMetric.RecentEscalations, a.Id, a.Number, c.FirstName, c.LastName, a.Status, a.AmountGel,
                        a.EscalatedAt!.Value, null, null))
                .Take(take).ToListAsync(cancellationToken),
        ];

        var actorIds = events.Select(e => e.By).OfType<Guid>().Distinct().ToList();
        var actors = await db.UserDirectory.AsNoTracking().Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        var changes = await AuditFeedAsync(accessChanges, take, cancellationToken);

        var feed = events
            .Select(e => new AttentionItem(
                AttentionKind.Application, e.ApplicationId.Value.ToString(),
                $"{LoanApplication.FormatReference(e.Number)} · {e.FirstName} {e.LastName}", e.Status, e.Reason, e.At, e.AmountGel, e.Kind,
                e.By is { } by ? actors.GetValueOrDefault(by) : null))
            .Concat(changes)
            .OrderByDescending(i => i.At)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .Take(take)
            .ToList();
        return new DashboardData(DashboardKind.Audit, tiles, feed);
    }

    public async Task<DashboardData> AdministrationAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        var since = ApplicationQueues.RecentSince(now);
        var users = db.UserDirectory.AsNoTracking();
        var accessChanges = AccessChanges(since);
        DashboardTile[] tiles =
        [
            new(DashboardMetric.LockedUsers, await users.WithStatus(UserStatusFilter.LockedOut, now).CountAsync(cancellationToken), Alert: true),
            new(DashboardMetric.UsersWithoutMfa, await users.WithStatus(UserStatusFilter.WithoutMfa, now).CountAsync(cancellationToken)),
            new(DashboardMetric.RecentAccessChanges, await accessChanges.CountAsync(cancellationToken)),
        ];

        // One feed, newest first: lockouts and access changes together.
        var lockouts = db.AuditLogs.AsNoTracking().Where(l => l.Action == AuditActions.LockedOut && l.OccurredAt >= since);
        var feed = await AuditFeedAsync(lockouts.Concat(accessChanges), take, cancellationToken);
        return new DashboardData(DashboardKind.Administration, tiles, feed);
    }

    /// <summary>Access changes of the recent window — the audit log's <c>AccessChanges</c> category from its first day.</summary>
    private IQueryable<AuditLog> AccessChanges(DateTimeOffset since) =>
        db.AuditLogs.AsNoTracking().InCategory(AuditCategory.AccessChanges).Where(l => l.OccurredAt >= since);

    private static async Task<List<AttentionItem>> AuditFeedAsync(IQueryable<AuditLog> logs, int take, CancellationToken cancellationToken)
    {
        var rows = await logs.OrderByDescending(l => l.OccurredAt).ThenByDescending(l => l.Id).Take(take)
            .Select(l => new { l.Id, l.Action, l.UserName, l.EntityType, l.EntityId, l.OccurredAt })
            .ToListAsync(cancellationToken);
        return rows.Select(l => new AttentionItem(
                AttentionKind.AuditRecord, l.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), l.Action, null,
                l.EntityType is null ? null : $"{l.EntityType} {l.EntityId}", l.OccurredAt, null,
                l.Action == AuditActions.LockedOut ? DashboardMetric.LockedUsers : DashboardMetric.RecentAccessChanges, l.UserName))
            .ToList();
    }

    private Task<int> CountAsync(ApplicationAccessFilter access, ApplicationQueueKind queue, DateTimeOffset now, CancellationToken cancellationToken) =>
        Visible(access).InQueue(queue, now, db, LargeDisbursementGel).CountAsync(cancellationToken);

    private IQueryable<LoanApplication> Visible(ApplicationAccessFilter access) =>
        db.LoanApplications.AsNoTracking().VisibleTo(access);

    /// <summary>One exception event of an application.</summary>
    private sealed record FeedRow(
        DashboardMetric Kind, LoanApplicationId ApplicationId, long Number, string FirstName, string LastName, ApplicationStatus Status,
        decimal? AmountGel, DateTimeOffset At, Guid? By, string? Reason);
}
