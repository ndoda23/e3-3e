using CreditFlow.Application.Common;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.Auditing;

public sealed record AuditLogSearchQuery
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public UserId? UserId { get; init; }

    public string? Action { get; init; }

    public string? EntityType { get; init; }

    /// <summary>Free text matched against user name and entity id.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = Paging.DefaultPageSize;

    public AuditLogSearchQuery Normalize() => this with
    {
        Page = Paging.NormalizePage(Page),
        PageSize = Paging.NormalizePageSize(PageSize),
        Action = string.IsNullOrWhiteSpace(Action) ? null : Action.Trim(),
        EntityType = string.IsNullOrWhiteSpace(EntityType) ? null : EntityType.Trim(),
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
    };
}

public sealed record AuditLogListItem(
    AuditLogId Id,
    DateTimeOffset OccurredAt,
    string Action,
    string? UserName,
    string? EntityType,
    string? EntityId,
    string? IpAddress);

public sealed record AuditLogDetails(
    AuditLogId Id,
    DateTimeOffset OccurredAt,
    string Action,
    Guid? UserId,
    string? UserName,
    string? EntityType,
    string? EntityId,
    string? BeforeJson,
    string? AfterJson,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId);

/// <summary>Read-only access to the audit log. There is deliberately no way to change or delete records.</summary>
public interface IAuditLogQueries
{
    Task<PagedResult<AuditLogListItem>> SearchAsync(AuditLogSearchQuery query, CancellationToken cancellationToken);

    Task<AuditLogDetails?> GetAsync(AuditLogId id, CancellationToken cancellationToken);

    /// <summary>Most recent records where the user acted or was the subject.</summary>
    Task<IReadOnlyList<AuditLogListItem>> ListRecentForUserAsync(
        UserId userId, int take, CancellationToken cancellationToken);

    /// <summary>Distinct action names, for the filter drop-down.</summary>
    Task<IReadOnlyList<string>> ListActionsAsync(CancellationToken cancellationToken);

    /// <summary>Distinct entity types, for the filter drop-down.</summary>
    Task<IReadOnlyList<string>> ListEntityTypesAsync(CancellationToken cancellationToken);
}
