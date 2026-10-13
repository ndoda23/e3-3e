using CreditFlow.Application.Applications;
using CreditFlow.Application.Dashboard;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Queries;

/// <summary>
/// Dashboard counts and short lists, each one SQL query over the applications the viewer may see
/// (<see cref="ApplicationScope.VisibleTo"/>, the rule the lists use).
/// </summary>
internal sealed class DashboardQueries(CreditFlowDbContext db) : IDashboardQueries
{
    /// <summary>Statuses where the operator has the next step.</summary>
    private static readonly ApplicationStatus[] OperatorActionable =
    [
        ApplicationStatus.DocumentsPending, ApplicationStatus.Approved, ApplicationStatus.ConditionallyApproved, ApplicationStatus.ConditionsMet,
    ];

    public async Task<DashboardData> OperatorAsync(
        ApplicationAccessFilter access, DateTimeOffset stuckBefore, int take, CancellationToken cancellationToken)
    {
        var visible = Visible(access);
        var byStatus = await visible.GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
        var payouts = await (
                from d in db.Disbursements
                join a in visible on d.ApplicationId equals a.Id
                where d.Status == DisbursementStatus.Pending || d.Status == DisbursementStatus.Failed
                group d by d.Status into g
                select new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
        var stuck = await visible.CountAsync(a => a.Status == ApplicationStatus.PreCheck && a.StatusChangedAt < stuckBefore, cancellationToken);

        int Of(ApplicationStatus status) => byStatus.GetValueOrDefault(status);
        var failed = payouts.GetValueOrDefault(DisbursementStatus.Failed);
        DashboardTile[] tiles =
        [
            new(DashboardMetric.Drafts, Of(ApplicationStatus.Draft)),
            new(DashboardMetric.DocumentsPending, Of(ApplicationStatus.DocumentsPending)),
            new(DashboardMetric.AwaitingSignature, Of(ApplicationStatus.Approved) + Of(ApplicationStatus.ConditionallyApproved)),
            new(DashboardMetric.AwaitingDisbursement, Of(ApplicationStatus.ConditionsMet)),
            new(DashboardMetric.PayoutsPending, payouts.GetValueOrDefault(DisbursementStatus.Pending)),
            new(DashboardMetric.PayoutsFailed, failed, Alert: true),
            new(DashboardMetric.StuckInPreCheck, stuck, Alert: true),
        ];

        // Failed payouts first, then stuck pre-checks, then whatever waits longest for the operator.
        var rows = await (
                from a in visible
                join c in db.Clients on a.BorrowerId equals c.Id
                let payoutFailed = db.Disbursements.Any(d => d.ApplicationId == a.Id && d.Status == DisbursementStatus.Failed)
                let stuckInPreCheck = a.Status == ApplicationStatus.PreCheck && a.StatusChangedAt < stuckBefore
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

    public async Task<IReadOnlyList<DashboardTile>> UnderwritingAsync(
        ApplicationAccessFilter access, bool withOverridable, CancellationToken cancellationToken)
    {
        var escalated = await Visible(access)
            .CountAsync(a => a.Status == ApplicationStatus.Underwriting && a.EscalatedAt != null, cancellationToken);
        if (!withOverridable)
        {
            return [new DashboardTile(DashboardMetric.Escalated, escalated)];
        }

        // The scoring runs are a JSON column: the latest run (highest array index) has a failed rule. In Underwriting
        // a failure is always overridable (a hard failure rejects the application automatically).
        var underwriting = nameof(ApplicationStatus.Underwriting);
        var overridable = await db.LoanApplications
            .FromSql($"""
                SELECT a.* FROM LoanApplications AS a
                WHERE a.Status = {underwriting} AND EXISTS (
                    SELECT 1 FROM OPENJSON(a.ScoringRunsJson) AS run
                    CROSS APPLY OPENJSON(run.value, '$.results') AS result
                    WHERE CAST(run.[key] AS int) = (SELECT MAX(CAST(latest.[key] AS int)) FROM OPENJSON(a.ScoringRunsJson) AS latest)
                      AND JSON_VALUE(result.value, '$.passed') = 'false')
                """)
            .AsNoTracking()
            .VisibleTo(access)
            .CountAsync(cancellationToken);
        return [new DashboardTile(DashboardMetric.Escalated, escalated), new DashboardTile(DashboardMetric.Overridable, overridable)];
    }

    public async Task<DashboardData> AuditAsync(
        ApplicationAccessFilter access, DateTimeOffset since, int take, CancellationToken cancellationToken)
    {
        var visible = Visible(access);
        var overrides =
            from a in visible
            from d in a.Decisions
            where d.OverrideReason != null && d.DecidedAt >= since
            join c in db.Clients on a.BorrowerId equals c.Id
            select new { a.Id, a.Number, c.FirstName, c.LastName, a.Status, d.OverrideReason, d.DecidedAt, a.AmountGel, DecisionId = d.Id };
        var payouts =
            from d in db.Disbursements
            join a in visible on d.ApplicationId equals a.Id
            join c in db.Clients on a.BorrowerId equals c.Id
            where d.Status == DisbursementStatus.Succeeded && d.CompletedAt >= since
            select new { a.Id, a.Number, c.FirstName, c.LastName, a.Status, d.CompletedAt, a.AmountGel, DisbursementId = d.Id };

        DashboardTile[] tiles =
        [
            new(DashboardMetric.RecentOverrides, await overrides.CountAsync(cancellationToken)),
            new(DashboardMetric.RecentRejections, await visible.CountAsync(a => a.Status == ApplicationStatus.Rejected && a.StatusChangedAt >= since, cancellationToken)),
            new(DashboardMetric.RecentDisbursements, await payouts.CountAsync(cancellationToken)),
        ];

        var latestOverrides = await overrides.OrderByDescending(o => o.DecidedAt).ThenBy(o => o.DecisionId).Take(take / 2).ToListAsync(cancellationToken);
        var largest = await payouts.OrderByDescending(p => p.AmountGel).ThenBy(p => p.DisbursementId).Take(take - latestOverrides.Count).ToListAsync(cancellationToken);
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

    public async Task<DashboardData> AdministrationAsync(
        DateTimeOffset now, DateTimeOffset since, int take, CancellationToken cancellationToken)
    {
        var users = db.UserDirectory.AsNoTracking();
        var locked = users.Where(u => u.LockoutEnd != null && u.LockoutEnd > now);
        var changes = db.AuditLogs.AsNoTracking().Where(l =>
            (l.Action == AuditActions.RolePermissionsChanged || l.Action == AuditActions.MfaRequirementChanged) && l.OccurredAt >= since);

        DashboardTile[] tiles =
        [
            new(DashboardMetric.LockedUsers, await locked.CountAsync(cancellationToken), Alert: true),
            new(DashboardMetric.UsersWithoutMfa, await users.CountAsync(u => u.IsActive && !u.TwoFactorEnabled, cancellationToken)),
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

    private IQueryable<LoanApplication> Visible(ApplicationAccessFilter access) =>
        db.LoanApplications.AsNoTracking().VisibleTo(access);
}
