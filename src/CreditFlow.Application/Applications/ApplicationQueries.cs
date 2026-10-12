using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.Applications;

public enum ApplicationSortField
{
    Updated = 0,
    Created = 1,
    Amount = 2,
    Status = 3,
    Borrower = 4,
}

/// <summary>Filters, sorting and paging of the application list (all applied in SQL).</summary>
public sealed record ApplicationSearch
{
    /// <summary>Borrower name, personal number or contract number.</summary>
    public string? Text { get; init; }

    public ApplicationStatus? Status { get; init; }

    public LoanProductId? ProductId { get; init; }

    public ApplicationSortField Sort { get; init; }

    public bool Descending { get; init; } = true;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = Paging.DefaultPageSize;

    public ApplicationSearch Normalize() => this with
    {
        Text = string.IsNullOrWhiteSpace(Text) ? null : Text.Trim(),
        Sort = Enum.IsDefined(Sort) ? Sort : ApplicationSortField.Updated,
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

public sealed record PartyView(ClientId ClientId, string Name, string PersonalNumber, PartyRole Role);

public sealed record CollateralView(ApplicationLineId Id, CollateralType Type, string Description, Money MarketValue, DateOnly ValuationDate);

public sealed record DocumentView(
    ApplicationLineId Id, DocumentType Type, string FileName, long Size, string UploadedBy, DateTimeOffset UploadedAt);

public sealed record HistoryView(ApplicationStatus? From, ApplicationStatus To, DateTimeOffset At, string By, string? Reason);

public sealed record DecisionView(DecisionKind Kind, string DecidedBy, UserId DecidedById, DateTimeOffset At, string? Reason, string? OverrideReason);

public sealed record ConditionView(ApplicationLineId Id, ConditionType Type, string Description, bool IsMet, DateTimeOffset? MetAt);

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

/// <summary>Current exposure of clients at the bank and their monthly installments here (for scoring and disbursement).</summary>
public interface IExposureQueries
{
    /// <summary>
    /// GEL exposure per client, excluding <paramref name="except"/>: approved and not yet disbursed applications, and
    /// disbursed or active loans, as borrower, co-borrower or guarantor.
    /// </summary>
    Task<IReadOnlyDictionary<ClientId, decimal>> ExposureGelAsync(
        IReadOnlyCollection<ClientId> clients, LoanApplicationId except, CancellationToken cancellationToken);

    /// <summary>Monthly installments (GEL) of each client's active loans at this bank, as borrower or co-borrower.</summary>
    Task<IReadOnlyDictionary<ClientId, decimal>> MonthlyInstallmentsGelAsync(
        IReadOnlyCollection<ClientId> clients, CancellationToken cancellationToken);
}
