using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using CreditFlow.Web.Models;
using Microsoft.AspNetCore.Mvc;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Controllers;

/// <summary>
/// The decision makers' queues: Underwriting, Credit Committee and Compliance, each shown to users holding its
/// permission. The queues are computed in SQL: never the viewer's own applications, never one they already decided,
/// and for underwriters only files of their tier.
/// </summary>
[HasAnyPermission(P.Underwriting.Decide, P.Committee.Vote, P.Compliance.Review)]
public sealed class InboxController(DecisionService decisions, CurrentAccess currentAccess) : Controller
{
    private static readonly (Inbox Inbox, string Permission)[] Queues =
    [
        (Inbox.Underwriting, P.Underwriting.Decide),
        (Inbox.Committee, P.Committee.Vote),
        (Inbox.Compliance, P.Compliance.Review),
    ];

    [HttpGet]
    public async Task<IActionResult> Index(Inbox? inbox, InboxFilter? filter, int page, CancellationToken cancellationToken)
    {
        var available = Available(await currentAccess.GetAsync());
        if (available.Count == 0)
        {
            return Forbid();
        }

        var current = inbox is { } requested && available.Contains(requested) ? requested : available[0];
        var active = current == Inbox.Underwriting && filter is { } wanted && Enum.IsDefined(wanted) ? wanted : (InboxFilter?)null;
        var model = new InboxViewModel(current, available, await decisions.InboxAsync(current, page, cancellationToken, active), active);
        return Request.IsHtmx() ? PartialView("_InboxRows", model) : View(model);
    }

    private static List<Inbox> Available(UserAccessProfile? profile) =>
        profile is null ? [] : Queues.Where(q => profile.HasPermission(q.Permission)).Select(q => q.Inbox).ToList();
}
