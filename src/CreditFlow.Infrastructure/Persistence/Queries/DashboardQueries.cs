using CreditFlow.Application.Applications;
using CreditFlow.Application.Dashboard;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Queries;

/// <summary>
/// Dashboard counts and short lists. Every count is the total of the list its tile links to: one status, one
/// <see cref="ApplicationQueueKind"/> (<see cref="ApplicationScope.InQueue"/>), one user status
/// (<see cref="UserStatusFilters.WithStatus"/>) or one audit filter — always within the viewer's access filter.
/// </summary>
internal sealed class DashboardQueries(CreditFlowDbContext db) : IDashboardQueries
{
    /// <summary>Statuses where the operator has the next step.</summary>
    private static readonly ApplicationStatus[] OperatorActionable =
    [
        ApplicationStatus.DocumentsPending, ApplicationStatus.Approved, ApplicationStatus.ConditionallyApproved, ApplicationStatus.ConditionsMet,
    ];

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

        // Failed payouts first, then stuck pre-checks, then whatever waits longest for the operator.
        var failed = visible.InQueue(ApplicationQueueKind.PayoutFailed, now, db).Select(a => a.Id);
        var stuck = visible.InQueue(ApplicationQueueKind.StuckInPreCheck, now, db).Select(a => a.Id);
        var rows = await (
                from a in visible
                join c in db.Clients on a.BorrowerId equals c.Id
                let payoutFailed = failed.Contains(a.Id)
                let stuckInPreCheck = stuck.Contains(a.Id)
                where payoutFailed || stuckInPreCheck || OperatorActionable.Contains(a.Status)
                orderby payoutFailed descending, stuckInPreCheck descending, a.StatusChangedAt, a.Id
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
        DashboardTile[] tiles =
        [
            new(DashboardMetric.RecentOverrides, await CountAsync(access, ApplicationQueueKind.RecentOverrides, now, cancellationToken)),
            new(DashboardMetric.RecentRejections, await CountAsync(access, ApplicationQueueKind.RecentlyRejected, now, cancellationToken)),
            new(DashboardMetric.RecentDisbursements, await CountAsync(access, ApplicationQueueKind.RecentlyDisbursed, now, cancellationToken)),
        ];

        var since = ApplicationQueues.RecentSince(now);
        var visible = Visible(access);
        var latestOverrides = await (
                from a in visible
                from d in a.Decisions
                where d.OverrideReason != null && d.DecidedAt >= since
                join c in db.Clients on a.BorrowerId equals c.Id
                orderby d.DecidedAt descending, d.Id
                select new { a.Id, a.Number, c.FirstName, c.LastName, a.Status, d.OverrideReason, d.DecidedAt, a.AmountGel })
            .Take(take / 2)
            .ToListAsync(cancellationToken);
        var largest = await (
                from d in db.Disbursements
                join a in visible on d.ApplicationId equals a.Id
                join c in db.Clients on a.BorrowerId equals c.Id
                where d.Status == DisbursementStatus.Succeeded && d.CompletedAt >= since
                orderby a.AmountGel descending, d.Id
                select new { a.Id, a.Number, c.FirstName, c.LastName, a.Status, d.CompletedAt, a.AmountGel })
            .Take(take - latestOverrides.Count)
            .ToListAsync(cancellationToken);

        AttentionItem[] attention =
        [
            .. latestOverrides.Select(o => new AttentionItem(
                AttentionKind.Application, o.Id.Value.ToString(), $"{LoanApplication.FormatReference(o.Number)} · {o.FirstName} {o.LastName}",
                o.Status, o.OverrideReason, o.DecidedAt, o.AmountGel, DashboardMetric.RecentOverrides)),
            .. largest.Select(p => new AttentionItem(
                AttentionKind.Application, p.Id.Value.ToString(), $"{LoanApplication.FormatReference(p.Number)} · {p.FirstName} {p.LastName}",
                p.Status, null, p.CompletedAt, p.AmountGel, DashboardMetric.RecentDisbursements)),
        ];
        return new DashboardData(DashboardKind.Audit, tiles, attention);
    }

    public async Task<DashboardData> AdministrationAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        var users = db.UserDirectory.AsNoTracking();
        var locked = users.WithStatus(UserStatusFilter.LockedOut, now);
        var since = ApplicationQueues.RecentSince(now); // the audit log's "from" filter: midnight in Tbilisi
        var changes = db.AuditLogs.AsNoTracking()
            .Where(l => l.Action == AuditActions.RolePermissionsChanged && l.OccurredAt >= since);

        DashboardTile[] tiles =
        [
            new(DashboardMetric.LockedUsers, await locked.CountAsync(cancellationToken), Alert: true),
            new(DashboardMetric.UsersWithoutMfa, await users.WithStatus(UserStatusFilter.WithoutMfa, now).CountAsync(cancellationToken)),
            new(DashboardMetric.RecentAccessChanges, await changes.CountAsync(cancellationToken)),
        ];

        var lockedUsers = await locked.OrderBy(u => u.FullName).ThenBy(u => u.Id).Take(take / 2)
            .Select(u => new { u.Id, u.FullName, u.UserName, u.LockoutEnd }).ToListAsync(cancellationToken);
        var latestChanges = await changes.OrderByDescending(l => l.OccurredAt).ThenByDescending(l => l.Id).Take(take - lockedUsers.Count)
            .Select(l => new { l.Id, l.Action, l.UserName, l.EntityId, l.OccurredAt }).ToListAsync(cancellationToken);
        AttentionItem[] attention =
        [
            .. lockedUsers.Select(u => new AttentionItem(
                AttentionKind.User, u.Id.ToString(), u.FullName, null, u.UserName, u.LockoutEnd, null, DashboardMetric.LockedUsers)),
            .. latestChanges.Select(l => new AttentionItem(
                AttentionKind.AuditRecord, l.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), l.Action, null,
                $"{l.UserName} · {l.EntityId}", l.OccurredAt, null, DashboardMetric.RecentAccessChanges)),
        ];
        return new DashboardData(DashboardKind.Administration, tiles, attention);
    }

    private Task<int> CountAsync(ApplicationAccessFilter access, ApplicationQueueKind queue, DateTimeOffset now, CancellationToken cancellationToken) =>
        Visible(access).InQueue(queue, now, db).CountAsync(cancellationToken);

    private IQueryable<LoanApplication> Visible(ApplicationAccessFilter access) =>
        db.LoanApplications.AsNoTracking().VisibleTo(access);
}
