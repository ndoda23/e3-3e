using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.Applications;

/// <summary>Sort orders of the application list.</summary>
public enum ApplicationSortField
{
    Updated = 0,
    Created = 1,
    Amount = 2,
    Status = 3,
    Borrower = 4,
}

/// <summary>
/// Named work queues of the application list. A dashboard tile counts exactly one queue (or one status) and links to
/// the list filtered by it, so the tile and the list always agree.
/// </summary>
public enum ApplicationQueueKind
{
    /// <summary>Approved or conditionally approved: the client signs next.</summary>
    AwaitingSignature = 1,

    /// <summary>A payout was sent and its outcome is not known yet.</summary>
    PayoutPending = 2,

    /// <summary>Core banking rejected the payout; the operator retries.</summary>
    PayoutFailed = 3,

    /// <summary>In pre-check for longer than <see cref="ApplicationQueues.StuckInPreCheckAfter"/>.</summary>
    StuckInPreCheck = 4,

    /// <summary>In Underwriting and escalated to a Senior Underwriter.</summary>
    Escalated = 5,

    /// <summary>In Underwriting and the latest scoring failed an overridable rule.</summary>
    NeedsOverride = 6,

    /// <summary>Rejected within the recent window.</summary>
    RecentlyRejected = 7,

    /// <summary>Approved with an override within the recent window.</summary>
    RecentOverrides = 8,

    /// <summary>Paid out within the recent window.</summary>
    RecentlyDisbursed = 9,
}

/// <summary>The time limits of the queues.</summary>
public static class ApplicationQueues
{
    /// <summary>The live pre-check answers within minutes; longer than this it waits for a service that is down.</summary>
    public static readonly TimeSpan StuckInPreCheckAfter = TimeSpan.FromMinutes(30);

    /// <summary>The "recent" queues cover this many whole bank days (today included).</summary>
    public const int RecentDays = 30;

    /// <summary>The first bank day of the recent window.</summary>
    public static DateOnly RecentFrom(DateTimeOffset now) => BankTime.DateOf(now).AddDays(-(RecentDays - 1));

    /// <summary>The start of the recent window (midnight in Tbilisi).</summary>
    public static DateTimeOffset RecentSince(DateTimeOffset now) => BankTime.ToInstant(RecentFrom(now), TimeOnly.MinValue);
}

/// <summary>Filters, sorting and paging of the application list (all applied in SQL).</summary>
public sealed record ApplicationSearch
{
    /// <summary>Borrower name, personal number, application reference (APP-000123) or contract number.</summary>
    public string? Text { get; init; }

    public ApplicationStatus? Status { get; init; }

    public ApplicationQueueKind? Queue { get; init; }

    /// <summary>Only applications where this client is a party (borrower, co-borrower or guarantor).</summary>
    public ClientId? ClientId { get; init; }

    public LoanProductId? ProductId { get; init; }

    public ApplicationSortField Sort { get; init; }

    public bool Descending { get; init; } = true;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = Paging.DefaultPageSize;

    public ApplicationSearch Normalize() => this with
    {
        Text = string.IsNullOrWhiteSpace(Text) ? null : Text.Trim(),
        Sort = Enum.IsDefined(Sort) ? Sort : ApplicationSortField.Updated,
        Queue = Queue is { } queue && Enum.IsDefined(queue) ? queue : null,
        Page = Paging.NormalizePage(Page),
        PageSize = Paging.NormalizePageSize(PageSize),
    };
}

/// <summary>The queues of the decision makers.</summary>
public enum Inbox
{
    /// <summary>Applications in Underwriting the user may decide (tier, escalation), not created or decided by them.</summary>
    Underwriting = 1,

    /// <summary>Applications at the Credit Committee the user has not voted on and did not create.</summary>
    Committee = 2,

    /// <summary>Applications held for a sanctions or PEP match the user did not create or decide.</summary>
    Compliance = 3,
}

/// <summary>One row of an application list or inbox.</summary>
public sealed record ApplicationListItem(
    LoanApplicationId Id,
    string Reference,
    ApplicationStatus Status,
    string ProductName,
    string BorrowerName,
    string BorrowerPersonalNumber,
    Money Amount,
    decimal? AmountGel,
    int TermMonths,
    string BranchName,
    ApprovalTier? Tier,
    DateTimeOffset CreatedAt,
    DateTimeOffset StatusChangedAt,
    DateTimeOffset? EscalatedAt,
    DateTimeOffset? OfferExpiresAt);

/// <summary>A party of an application.</summary>
public sealed record PartyView(ClientId ClientId, string Name, string PersonalNumber, PartyRole Role);

/// <summary>A collateral item of an application.</summary>
public sealed record CollateralView(ApplicationLineId Id, CollateralType Type, string Description, Money MarketValue, DateOnly ValuationDate);

/// <summary>Metadata of an uploaded document.</summary>
public sealed record DocumentView(
    ApplicationLineId Id, DocumentType Type, string FileName, long Size, string UploadedBy, DateTimeOffset UploadedAt);

/// <summary>One status transition (who, when, why).</summary>
public sealed record HistoryView(ApplicationStatus? From, ApplicationStatus To, DateTimeOffset At, string By, string? Reason);

/// <summary>A decision taken on the application.</summary>
public sealed record DecisionView(DecisionKind Kind, string DecidedBy, UserId DecidedById, DateTimeOffset At, string? Reason, string? OverrideReason);

/// <summary>A condition to meet before disbursement.</summary>
public sealed record ConditionView(ApplicationLineId Id, ConditionType Type, string Description, bool IsMet, DateTimeOffset? MetAt);

/// <summary>The state of the payout of an application.</summary>
public sealed record PayoutView(
    DisbursementStatus Status, Money Amount, int Attempts, string? TransactionId, string? LastError, DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Everything the application page shows.</summary>
public sealed record ApplicationDetails
{
    public required ApplicationListItem Summary { get; init; }

    public required UserId CreatedBy { get; init; }

    public required string CreatedByName { get; init; }

    public required ProductSnapshot? Snapshot { get; init; }

    public required string ProductCode { get; init; }

    public required LoanTerms Terms { get; init; }

    public required LoanTerms? ProposedTerms { get; init; }

    /// <summary>The tier needed to decide now (escalated files need a Senior Underwriter); <c>null</c> before scoring.</summary>
    public required ApprovalTier? RequiredTier { get; init; }

    public required string? ContractNumber { get; init; }

    public required bool DisbursementRequested { get; init; }

    public required IReadOnlyList<PartyView> Parties { get; init; }

    public required IReadOnlyList<CollateralView> Collaterals { get; init; }

    public required IReadOnlyList<DocumentView> Documents { get; init; }

    public required IReadOnlyList<DocumentType> MissingDocuments { get; init; }

    public required IReadOnlyList<HistoryView> History { get; init; }

    public required IReadOnlyList<DecisionView> Decisions { get; init; }

    public required IReadOnlyList<ConditionView> Conditions { get; init; }

    public required ScoringRun? LatestScoring { get; init; }

    /// <summary>The payout, once requested.</summary>
    public required PayoutView? Payout { get; init; }

    /// <summary>Matching committee votes that decide (from the approval thresholds).</summary>
    public required int RequiredVotes { get; init; }
}

/// <summary>Read side of applications. Every method applies the caller's <see cref="ApplicationAccessFilter"/>.</summary>
public interface IApplicationQueries
{
    Task<PagedResult<ApplicationListItem>> SearchAsync(
        ApplicationSearch search, ApplicationAccessFilter access, CancellationToken cancellationToken);

    /// <param name="inbox">Which queue.</param>
    /// <param name="access">The viewer's access.</param>
    /// <param name="maxTier">For the underwriting queue: the highest tier the viewer may decide.</param>
    /// <param name="page">Page number.</param>
    /// <param name="pageSize">Page size.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<PagedResult<ApplicationListItem>> InboxAsync(
        Inbox inbox, ApplicationAccessFilter access, ApprovalTier? maxTier, int page, int pageSize,
        CancellationToken cancellationToken);

    /// <summary>The application, or <c>null</c> when it does not exist or the viewer may not see it.</summary>
    Task<ApplicationDetails?> GetDetailsAsync(
        LoanApplicationId id, ApplicationAccessFilter access, CancellationToken cancellationToken);
}

/// <summary>A product that can be applied for, with the terms of its current version.</summary>
public sealed record ProductOption(LoanProductId Id, string Code, string Name, ProductType Type, ProductTerms Terms);

/// <summary>Read side of loan products.</summary>
public interface IProductQueries
{
    /// <summary>Active products by name.</summary>
    Task<IReadOnlyList<ProductOption>> ListActiveAsync(CancellationToken cancellationToken);
}

/// <summary>Current exposure of clients at the bank and their monthly installments here (for scoring and disbursement).</summary>
public interface IExposureQueries
{
    /// <summary>
    /// GEL exposure per client, excluding <paramref name="except"/>: approved and not yet disbursed applications, and
    /// disbursed or active loans, as borrower, co-borrower or guarantor.
    /// </summary>
    Task<IReadOnlyDictionary<ClientId, decimal>> ExposureGelAsync(
        IReadOnlyCollection<ClientId> clients, LoanApplicationId except, CancellationToken cancellationToken);

    /// <summary>
    /// GEL exposure per client from money that is committed, excluding <paramref name="except"/>: payouts pending or
    /// done whose loan is not booked yet, and the outstanding principal of active loans, as any party. Used by the
    /// re-check at disbursement (approvals that were never paid out do not block a payout).
    /// </summary>
    Task<IReadOnlyDictionary<ClientId, decimal>> CommittedExposureGelAsync(
        IReadOnlyCollection<ClientId> clients, LoanApplicationId except, CancellationToken cancellationToken);

    /// <summary>Monthly installments (GEL) of each client's active loans at this bank, as borrower or co-borrower.</summary>
    Task<IReadOnlyDictionary<ClientId, decimal>> MonthlyInstallmentsGelAsync(
        IReadOnlyCollection<ClientId> clients, CancellationToken cancellationToken);
}
