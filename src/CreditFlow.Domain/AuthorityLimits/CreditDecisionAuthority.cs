using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.AuthorityLimits;

public enum CreditDecisionOutcome
{
    /// <summary>The user may approve or decline.</summary>
    Within = 1,

    /// <summary>The amount is above the user's limit, or no limit is defined: the user can only escalate.</summary>
    Escalate = 2,

    /// <summary>The user may not take credit decisions at all.</summary>
    NotAuthorized = 3,
}

/// <param name="Outcome">What the user may do.</param>
/// <param name="Limit">The applicable maximum amount, when a limit row matched.</param>
public sealed record CreditDecisionEvaluation(CreditDecisionOutcome Outcome, decimal? Limit);

/// <summary>
/// Credit decision authority (FRD Flow C): a decider approves up to the limit for their role, level and currency;
/// above it, or when no limit row matches, the file is escalated so that it is never stuck.
/// </summary>
public static class CreditDecisionAuthority
{
    public static CreditDecisionEvaluation Evaluate(
        bool canDecide,
        RoleId roleId,
        AuthorityLevel? authorityLevel,
        IEnumerable<AuthorityLimit> limits,
        Currency currency,
        decimal amount)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(amount, 0m);

        if (!canDecide)
        {
            return new CreditDecisionEvaluation(CreditDecisionOutcome.NotAuthorized, null);
        }

        var limit = limits.FirstOrDefault(l => l.AppliesToCreditDecision(roleId, authorityLevel, currency));
        if (limit?.MaxAmount is not { } maxAmount)
        {
            return new CreditDecisionEvaluation(CreditDecisionOutcome.Escalate, null);
        }

        return new CreditDecisionEvaluation(
            amount <= maxAmount ? CreditDecisionOutcome.Within : CreditDecisionOutcome.Escalate,
            maxAmount);
    }
}
