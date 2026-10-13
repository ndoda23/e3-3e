using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Roles;

namespace CreditFlow.Application.Dashboard;

/// <summary>What a dashboard tile counts. The web layer turns each into a label and a link to the filtered list.</summary>
public enum DashboardMetric
{
    Drafts = 1,
    DocumentsPending,
    AwaitingSignature,
    AwaitingDisbursement,
    PayoutsPending,
    PayoutsFailed,
    StuckInPreCheck,
    Inbox,
    Escalated,
    Overridable,
    AwaitingVote,
    ComplianceReviews,
    RecentOverrides,
    RecentRejections,
    RecentDisbursements,
    LockedUsers,
    UsersWithoutMfa,
    RecentAccessChanges,
}

/// <param name="Metric">What is counted.</param>
/// <param name="Count">How many.</param>
/// <param name="Alert">A non-zero count means something went wrong (failed payout, stuck pre-check, locked user).</param>
public sealed record DashboardTile(DashboardMetric Metric, int Count, bool Alert = false);

public enum AttentionKind
{
    Application = 1,
    User,
    AuditRecord,
}

/// <summary>One line of the "needs your attention" list.</summary>
/// <param name="Kind">What the line links to.</param>
/// <param name="Id">The application, user or audit record id, as used in its link.</param>
/// <param name="Title">Reference and borrower, user name, or the audited action.</param>
/// <param name="Status">The application's status, when the line is an application.</param>
/// <param name="Note">Why it is listed (override reason, payout error, who changed what).</param>
/// <param name="At">Since when it waits, or when it happened.</param>
/// <param name="AmountGel">The amount in GEL, when relevant.</param>
/// <param name="Reason">The tile this line explains (e.g. a failed payout), when it is listed for that reason.</param>
public sealed record AttentionItem(
    AttentionKind Kind, string Id, string Title, ApplicationStatus? Status, string? Note, DateTimeOffset? At, decimal? AmountGel,
    DashboardMetric? Reason = null);

/// <param name="Kind">The role's dashboard.</param>
/// <param name="Tiles">The KPI row.</param>
/// <param name="Attention">The short list, most urgent first.</param>
/// <param name="OldestWaitingSince">For decision inboxes: since when the oldest item waits.</param>
public sealed record DashboardData(
    DashboardKind Kind, IReadOnlyList<DashboardTile> Tiles, IReadOnlyList<AttentionItem> Attention, DateTimeOffset? OldestWaitingSince = null);

/// <summary>The SQL side of the dashboards; every application count is limited by the viewer's access filter.</summary>
public interface IDashboardQueries
{
    Task<DashboardData> OperatorAsync(ApplicationAccessFilter access, DateTimeOffset stuckBefore, int take, CancellationToken cancellationToken);

    /// <summary>Escalated files and, when <paramref name="withOverridable"/>, files whose latest scoring needs an override.</summary>
    Task<IReadOnlyList<DashboardTile>> UnderwritingAsync(ApplicationAccessFilter access, bool withOverridable, CancellationToken cancellationToken);

    Task<DashboardData> AuditAsync(ApplicationAccessFilter access, DateTimeOffset since, int take, CancellationToken cancellationToken);

    Task<DashboardData> AdministrationAsync(DateTimeOffset now, DateTimeOffset since, int take, CancellationToken cancellationToken);
}

/// <summary>
/// The home page of each role: KPI tiles and a short list, computed in SQL within the user's data scope (the same
/// <see cref="ApplicationAccessFilter"/> as the lists, and the same inbox rules as the inbox pages).
/// </summary>
public sealed class DashboardService(
    CurrentAccessProfile access, IDashboardQueries queries, IApplicationQueries applications, TimeProvider clock)
{
    public const int AttentionSize = 6;

    /// <summary>The live pre-check answers within minutes; longer than this it waits for a service that is down.</summary>
    public static readonly TimeSpan StuckInPreCheckAfter = TimeSpan.FromMinutes(30);

    /// <summary>The window of the Auditor's and Administrator's "recent" tiles.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(30);

    /// <summary>The dashboard, or <c>null</c> when nobody (active) is signed in.</summary>
    public async Task<DashboardData?> GetAsync(CancellationToken cancellationToken)
    {
        var profile = await access.GetAsync(cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var filter = ApplicationAccessFilter.For(profile);
        var now = clock.GetUtcNow();
        return profile.Dashboard switch
        {
            DashboardKind.Operator => await queries.OperatorAsync(filter, now - StuckInPreCheckAfter, AttentionSize, cancellationToken),
            DashboardKind.Underwriting => await UnderwritingAsync(profile, filter, cancellationToken),
            DashboardKind.Committee => await InboxDashboardAsync(
                DashboardKind.Committee, Inbox.Committee, DashboardMetric.AwaitingVote, profile, filter, Permissions.Committee.Vote, [], cancellationToken),
            DashboardKind.Compliance => await InboxDashboardAsync(
                DashboardKind.Compliance, Inbox.Compliance, DashboardMetric.ComplianceReviews, profile, filter, Permissions.Compliance.Review, [], cancellationToken),
            DashboardKind.Audit => await queries.AuditAsync(filter, now - RecentWindow, AttentionSize, cancellationToken),
            DashboardKind.Administration when profile.HasPermission(Permissions.Admin.UsersView) =>
                await queries.AdministrationAsync(now, now - RecentWindow, AttentionSize, cancellationToken),
            _ => new DashboardData(profile.Dashboard, [], []),
        };
    }

    private async Task<DashboardData> UnderwritingAsync(UserAccessProfile profile, ApplicationAccessFilter filter, CancellationToken cancellationToken)
    {
        var senior = CurrentAccessProfile.AuthorityOf(profile)?.MaxTier == ApprovalTier.SeniorUnderwriter;
        var extra = profile.HasPermission(Permissions.Underwriting.Decide)
            ? await queries.UnderwritingAsync(filter, withOverridable: senior, cancellationToken)
            : [];
        return await InboxDashboardAsync(
            DashboardKind.Underwriting, Inbox.Underwriting, DashboardMetric.Inbox, profile, filter, Permissions.Underwriting.Decide, extra, cancellationToken);
    }

    /// <summary>A decision inbox: its size, the oldest waiting item, and its first items (the inbox page's own query).</summary>
    private async Task<DashboardData> InboxDashboardAsync(
        DashboardKind kind, Inbox inbox, DashboardMetric metric, UserAccessProfile profile, ApplicationAccessFilter filter,
        string permission, IReadOnlyList<DashboardTile> extra, CancellationToken cancellationToken)
    {
        if (!profile.HasPermission(permission))
        {
            return new DashboardData(kind, [], []);
        }

        var page = await applications.InboxAsync(
            inbox, filter, CurrentAccessProfile.AuthorityOf(profile)?.MaxTier, 1, AttentionSize, cancellationToken);
        var items = page.Items
            .Select(i => new AttentionItem(AttentionKind.Application, i.Id.Value.ToString(), $"{i.Reference} · {i.BorrowerName}", i.Status, i.ProductName, i.StatusChangedAt, i.AmountGel))
            .ToList();
        return new DashboardData(kind, [new DashboardTile(metric, page.TotalCount), .. extra], items, page.Items.Count > 0 ? page.Items[0].StatusChangedAt : null);
    }
}
