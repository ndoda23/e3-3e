using Lams.Application.Auditing;
using Lams.Application.Common;
using Lams.Domain.Auditing;
using Lams.Domain.Users;
using Lams.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lams.Infrastructure.Auditing;

internal sealed class AuditLogQueries(LamsDbContext db) : IAuditLogQueries
{
    /// <summary>Entity type used for user-related audit records (data changes and security events).</summary>
    private const string UserEntityType = "User";

    public async Task<PagedResult<AuditLogListItem>> SearchAsync(
        AuditLogSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query = query.Normalize();

        var logs = db.AuditLogs.AsNoTracking();
        if (query.From is { } from)
        {
            logs = logs.Where(l => l.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            logs = logs.Where(l => l.OccurredAt <= to);
        }

        if (query.UserId is { } userId)
        {
            logs = logs.Where(l => l.UserId == userId.Value);
        }

        if (query.Action is { } action)
        {
            logs = logs.Where(l => l.Action == action);
        }

        if (query.EntityType is { } entityType)
        {
            logs = logs.Where(l => l.EntityType == entityType);
        }

        if (query.Search is { } search)
        {
            logs = logs.Where(l => (l.UserName != null && l.UserName.Contains(search))
                                   || (l.EntityId != null && l.EntityId.Contains(search)));
        }

        var total = await logs.CountAsync(cancellationToken);
        var items = await logs
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(l => new AuditLogListItem(l.Id, l.OccurredAt, l.Action, l.UserName, l.EntityType, l.EntityId, l.IpAddress))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogListItem>(items, total, query.Page, query.PageSize);
    }

    public Task<AuditLogDetails?> GetAsync(AuditLogId id, CancellationToken cancellationToken) =>
        db.AuditLogs.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new AuditLogDetails(
                l.Id, l.OccurredAt, l.Action, l.UserId, l.UserName, l.EntityType, l.EntityId, l.BeforeJson,
                l.AfterJson, l.IpAddress, l.UserAgent, l.CorrelationId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AuditLogListItem>> ListRecentForUserAsync(
        UserId userId, int take, CancellationToken cancellationToken)
    {
        var id = userId.Value;
        var key = userId.ToString();

        return await db.AuditLogs.AsNoTracking()
            .Where(l => l.UserId == id || (l.EntityType == UserEntityType && l.EntityId == key))
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.Id)
            .Take(Math.Clamp(take, 1, Paging.MaxPageSize))
            .Select(l => new AuditLogListItem(l.Id, l.OccurredAt, l.Action, l.UserName, l.EntityType, l.EntityId, l.IpAddress))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListActionsAsync(CancellationToken cancellationToken) =>
        await db.AuditLogs.AsNoTracking().Select(l => l.Action).Distinct().OrderBy(a => a).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListEntityTypesAsync(CancellationToken cancellationToken) =>
        await db.AuditLogs.AsNoTracking()
            .Where(l => l.EntityType != null)
            .Select(l => l.EntityType!)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(cancellationToken);
}
