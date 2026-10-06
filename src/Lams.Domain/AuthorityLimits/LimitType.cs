namespace Lams.Domain.AuthorityLimits;

public enum LimitType
{
    /// <summary>Maximum loan amount a decider may approve (FRD Flow C).</summary>
    CreditDecision = 1,

    /// <summary>Conditions under which an approver may approve a pricing exception (FRD Flow B).</summary>
    PricingException = 2,
}
