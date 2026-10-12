using CreditFlow.Application.Authorization;

namespace CreditFlow.Web.Models;

/// <param name="Profile">The signed-in user's access profile.</param>
/// <param name="ShowsAuthorityLimit">The user takes credit decisions, so the dashboard shows their limit.</param>
/// <param name="CreditLimits">The user's credit decision limits (empty: none set, decisions are escalated).</param>
public sealed record DashboardViewModel(
    UserAccessProfile Profile,
    bool ShowsAuthorityLimit,
    IReadOnlyList<CreditDecisionLimit> CreditLimits);
