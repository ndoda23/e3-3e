using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Dashboard;
using CreditFlow.Domain.Applications;
using Microsoft.AspNetCore.Mvc;

namespace CreditFlow.Web.Models;

/// <param name="Profile">The signed-in user's access profile.</param>
/// <param name="Data">The role's tiles and attention list.</param>
public sealed record DashboardViewModel(UserAccessProfile Profile, DashboardData Data)
{
    /// <summary>Where a tile leads: the list or inbox filtered to what it counts (<c>null</c>: no list for it).</summary>
    public static string? Link(IUrlHelper url, DashboardMetric metric)
    {
        ArgumentNullException.ThrowIfNull(url);
        return metric switch
        {
            DashboardMetric.Drafts => Applications(url, ApplicationStatus.Draft),
            DashboardMetric.DocumentsPending => Applications(url, ApplicationStatus.DocumentsPending),
            DashboardMetric.AwaitingSignature => Applications(url, ApplicationStatus.Approved),
            DashboardMetric.AwaitingDisbursement or DashboardMetric.PayoutsPending or DashboardMetric.PayoutsFailed =>
                Applications(url, ApplicationStatus.ConditionsMet),
            DashboardMetric.StuckInPreCheck => Applications(url, ApplicationStatus.PreCheck),
            DashboardMetric.Inbox or DashboardMetric.Escalated or DashboardMetric.Overridable =>
                url.Action("Index", "Inbox", new { area = "", inbox = Inbox.Underwriting }),
            DashboardMetric.AwaitingVote => url.Action("Index", "Inbox", new { area = "", inbox = Inbox.Committee }),
            DashboardMetric.ComplianceReviews => url.Action("Index", "Inbox", new { area = "", inbox = Inbox.Compliance }),
            DashboardMetric.RecentRejections => Applications(url, ApplicationStatus.Rejected),
            DashboardMetric.RecentDisbursements => url.Action("Index", "Loans", new { area = "" }),
            DashboardMetric.LockedUsers => url.Action("Index", "Users", new { area = "Admin", status = "LockedOut" }),
            DashboardMetric.UsersWithoutMfa => url.Action("Index", "Users", new { area = "Admin" }),
            DashboardMetric.RecentAccessChanges => url.Action("Index", "AuditLog", new { area = "Admin", @event = "RolePermissionsChanged" }),
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

    private static string? Applications(IUrlHelper url, ApplicationStatus status) =>
        url.Action("Index", "Applications", new { area = "", status });
}
