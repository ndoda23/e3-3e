namespace CreditFlow.Domain.Applications;

/// <summary>The statuses of a loan application (the 8 steps of the CreditFlow process).</summary>
public enum ApplicationStatus
{
    Draft = 1,
    Submitted = 2,
    PreCheck = 3,
    ComplianceReview = 4,
    DocumentsPending = 5,
    Scoring = 6,
    Underwriting = 7,
    CreditCommittee = 8,
    CounterOffered = 9,
    Approved = 10,
    ConditionallyApproved = 11,
    ContractSigned = 12,
    ConditionsMet = 13,
    Disbursed = 14,
    Active = 15,
    Closed = 16,
    Rejected = 17,
    Expired = 18,
    Cancelled = 19,
}

/// <summary>
/// The only allowed status transitions. <see cref="LoanApplication"/> moves only along these edges; every other
/// transition is impossible. Cancellation is allowed from every status before disbursement.
/// </summary>
public static class ApplicationStateMachine
{
    /// <summary>Statuses from which the application can still be cancelled (before disbursement).</summary>
    public static readonly IReadOnlySet<ApplicationStatus> Cancellable = new HashSet<ApplicationStatus>
    {
        ApplicationStatus.Draft, ApplicationStatus.Submitted, ApplicationStatus.PreCheck,
        ApplicationStatus.ComplianceReview, ApplicationStatus.DocumentsPending, ApplicationStatus.Scoring,
        ApplicationStatus.Underwriting, ApplicationStatus.CreditCommittee, ApplicationStatus.CounterOffered,
        ApplicationStatus.Approved, ApplicationStatus.ConditionallyApproved, ApplicationStatus.ContractSigned,
        ApplicationStatus.ConditionsMet,
    };

    /// <summary>Open applications: not finished and not yet a loan (one per client and product).</summary>
    public static readonly IReadOnlySet<ApplicationStatus> Open = Cancellable;

    private static readonly Dictionary<ApplicationStatus, ApplicationStatus[]> Edges = new()
    {
        [ApplicationStatus.Draft] = [ApplicationStatus.Submitted],
        [ApplicationStatus.Submitted] = [ApplicationStatus.PreCheck],
        [ApplicationStatus.PreCheck] =
            [ApplicationStatus.ComplianceReview, ApplicationStatus.Rejected, ApplicationStatus.DocumentsPending],
        [ApplicationStatus.ComplianceReview] = [ApplicationStatus.DocumentsPending, ApplicationStatus.Rejected],
        [ApplicationStatus.DocumentsPending] = [ApplicationStatus.Scoring],
        [ApplicationStatus.Scoring] =
            [ApplicationStatus.Rejected, ApplicationStatus.Underwriting, ApplicationStatus.CreditCommittee],
        [ApplicationStatus.Underwriting] =
        [
            ApplicationStatus.Approved, ApplicationStatus.ConditionallyApproved, ApplicationStatus.Rejected,
            ApplicationStatus.CounterOffered,
        ],
        [ApplicationStatus.CreditCommittee] =
            [ApplicationStatus.Approved, ApplicationStatus.ConditionallyApproved, ApplicationStatus.Rejected],
        [ApplicationStatus.CounterOffered] = [ApplicationStatus.Scoring, ApplicationStatus.Expired],
        [ApplicationStatus.Approved] = [ApplicationStatus.ContractSigned, ApplicationStatus.Expired],
        [ApplicationStatus.ConditionallyApproved] = [ApplicationStatus.ContractSigned, ApplicationStatus.Expired],
        [ApplicationStatus.ContractSigned] = [ApplicationStatus.ConditionsMet],
        [ApplicationStatus.ConditionsMet] = [ApplicationStatus.Disbursed],
        [ApplicationStatus.Disbursed] = [ApplicationStatus.Active],
        [ApplicationStatus.Active] = [ApplicationStatus.Closed],
    };

    public static bool CanTransition(ApplicationStatus from, ApplicationStatus to) =>
        (to == ApplicationStatus.Cancelled && Cancellable.Contains(from))
        || (Edges.TryGetValue(from, out var targets) && targets.Contains(to));

    public static bool IsTerminal(ApplicationStatus status) =>
        status is ApplicationStatus.Closed or ApplicationStatus.Rejected or ApplicationStatus.Expired
            or ApplicationStatus.Cancelled;
}
