using System.Globalization;
using CreditFlow.Application.Applications;
using CreditFlow.Application.Auditing;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Dashboard;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Applications;
using Microsoft.AspNetCore.Mvc;

namespace CreditFlow.Web.Models;

/// <param name="Profile">The signed-in user's access profile.</param>
/// <param name="Data">The role's tiles and attention list.</param>
public sealed record DashboardViewModel(UserAccessProfile Profile, DashboardData Data)
{
    /// <summary>
    /// Where a tile leads: the list filtered to exactly what the tile counts — one status, one named queue, one inbox,
    /// one user status or one audit filter — so the list's total is the tile's number.
    /// </summary>
    public static string? Link(IUrlHelper url, DashboardMetric metric, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(url);
        return metric switch
        {
            DashboardMetric.Drafts => Status(url, ApplicationStatus.Draft),
            DashboardMetric.DocumentsPending => Status(url, ApplicationStatus.DocumentsPending),
            DashboardMetric.AwaitingSignature => Queue(url, ApplicationQueueKind.AwaitingSignature),
            DashboardMetric.AwaitingDisbursement => Status(url, ApplicationStatus.ConditionsMet),
            DashboardMetric.PayoutsPending => Queue(url, ApplicationQueueKind.PayoutPending),
            DashboardMetric.PayoutsFailed => Queue(url, ApplicationQueueKind.PayoutFailed),
            DashboardMetric.StuckInPreCheck => Queue(url, ApplicationQueueKind.StuckInPreCheck),
            DashboardMetric.Inbox => url.Action("Index", "Inbox", new { area = "", inbox = Inbox.Underwriting }),
            DashboardMetric.Escalated => url.Action("Index", "Inbox", new { area = "", inbox = Inbox.Underwriting, filter = InboxFilter.Escalated }),
            DashboardMetric.Overridable => url.Action("Index", "Inbox", new { area = "", inbox = Inbox.Underwriting, filter = InboxFilter.NeedsOverride }),
            DashboardMetric.AwaitingVote => url.Action("Index", "Inbox", new { area = "", inbox = Inbox.Committee }),
            DashboardMetric.ComplianceReviews => url.Action("Index", "Inbox", new { area = "", inbox = Inbox.Compliance }),
            DashboardMetric.RecentOverrides => Queue(url, ApplicationQueueKind.RecentOverrides),
            DashboardMetric.RecentRejections => Queue(url, ApplicationQueueKind.RecentlyRejected),
            DashboardMetric.ComplianceClearances => Queue(url, ApplicationQueueKind.RecentComplianceClearances),
            DashboardMetric.LargeDisbursements => Queue(url, ApplicationQueueKind.LargeDisbursements),
            DashboardMetric.PayoutIncidents => Queue(url, ApplicationQueueKind.PayoutIncidents),
            DashboardMetric.RecentEscalations => Queue(url, ApplicationQueueKind.RecentlyEscalated),
            DashboardMetric.LockedUsers => url.Action("Index", "Users", new { area = "Admin", status = UserStatusFilter.LockedOut }),
            DashboardMetric.UsersWithoutMfa => url.Action("Index", "Users", new { area = "Admin", status = UserStatusFilter.WithoutMfa }),
            DashboardMetric.RecentAccessChanges => url.Action("Index", "AuditLog", new
            {
                area = "Admin",
                category = AuditCategory.AccessChanges,
                from = ApplicationQueues.RecentFrom(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }),
            _ => null,
        };
    }

    /// <summary>The page of an attention line.</summary>
    public static string? Link(IUrlHelper url, AttentionItem item)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind switch
        {
            AttentionKind.Application => url.Action("Details", "Applications", new { area = "", id = item.Id }),
            AttentionKind.User => url.Action("Details", "Users", new { area = "Admin", id = item.Id }),
            AttentionKind.AuditRecord => url.Action("Details", "AuditLog", new { area = "Admin", id = item.Id }),
            _ => null,
        };
    }

    /// <summary>How long something has waited, e.g. <c>3 d 4 h</c> or <c>45 min</c>.</summary>
    public static string Age(DateTimeOffset since, DateTimeOffset now)
    {
        var age = now - since;
        return age.TotalDays >= 1 ? $"{(int)age.TotalDays} d {age.Hours} h"
            : age.TotalHours >= 1 ? $"{(int)age.TotalHours} h {age.Minutes} min"
            : $"{Math.Max(0, (int)age.TotalMinutes)} min";
    }

    private static string? Status(IUrlHelper url, ApplicationStatus status) =>
        url.Action("Index", "Applications", new { area = "", status });

    private static string? Queue(IUrlHelper url, ApplicationQueueKind queue) =>
        url.Action("Index", "Applications", new { area = "", queue });
}
