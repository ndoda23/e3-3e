using System.ComponentModel.DataAnnotations;
using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Clients;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using Microsoft.AspNetCore.Routing;

namespace CreditFlow.Web.Models;

// ----- Clients -----

/// <summary>Client registration: personal data and the identity document the operator checked.</summary>
public sealed class ClientForm
{
    [Display(Name = "Field.PersonalNumber")]
    public string? PersonalNumber { get; set; }

    [Display(Name = "Field.FirstName")]
    public string? FirstName { get; set; }

    [Display(Name = "Field.LastName")]
    public string? LastName { get; set; }

    [Display(Name = "Field.BirthDate")]
    public DateOnly? BirthDate { get; set; }

    [Display(Name = "Field.Phone")]
    public string? Phone { get; set; }

    [Display(Name = "Field.Email")]
    public string? Email { get; set; }

    [Display(Name = "Field.Address")]
    public string? Address { get; set; }

    [Display(Name = "Field.DocumentType")]
    public IdentityDocumentType DocumentType { get; set; } = IdentityDocumentType.IdCard;

    [Display(Name = "Field.DocumentNumber")]
    public string? DocumentNumber { get; set; }

    [Display(Name = "Field.DocumentIssuedOn")]
    public DateOnly? DocumentIssuedOn { get; set; }

    [Display(Name = "Field.DocumentExpiresOn")]
    public DateOnly? DocumentExpiresOn { get; set; }

    public ClientDetails ToDetails() =>
        new(FirstName, LastName, BirthDate ?? DateOnly.MinValue, Phone, Email, Address);

    public RegisterClientCommand ToCommand() => new(
        PersonalNumber, ToDetails(), DocumentType, DocumentNumber,
        DocumentIssuedOn ?? DateOnly.MinValue, DocumentExpiresOn ?? DateOnly.MinValue);

    public static ClientForm From(ClientDetailsView client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new()
        {
            PersonalNumber = client.PersonalNumber,
            FirstName = client.Details.FirstName,
            LastName = client.Details.LastName,
            BirthDate = client.Details.BirthDate,
            Phone = client.Details.Phone,
            Email = client.Details.Email,
            Address = client.Details.Address,
        };
    }
}

/// <summary>A replacement identity document (KYC data entered by the operator).</summary>
public sealed class IdentityDocumentForm
{
    public IdentityDocumentType Type { get; set; } = IdentityDocumentType.IdCard;

    public string? Number { get; set; }

    public DateOnly? IssuedOn { get; set; }

    public DateOnly? ExpiresOn { get; set; }
}

/// <summary>A monthly income of the client.</summary>
public sealed class IncomeForm
{
    public IncomeType Type { get; set; } = IncomeType.Salary;

    public string? Source { get; set; }

    public decimal MonthlyAmount { get; set; }

    public Currency Currency { get; set; } = Currency.GEL;
}

/// <summary>An obligation the client declared (other lenders).</summary>
public sealed class ObligationForm
{
    public string? Lender { get; set; }

    public decimal MonthlyPayment { get; set; }

    public decimal Outstanding { get; set; }

    public Currency Currency { get; set; } = Currency.GEL;
}

/// <summary>The client list with its search text.</summary>
public sealed record ClientIndexViewModel(string? Search, PagedResult<ClientListItem> Clients);

/// <summary>The client page; <paramref name="CanManage"/> shows the edit forms.</summary>
public sealed record ClientPageViewModel(ClientDetailsView Client, bool CanManage);

// ----- Applications -----

/// <summary>Application list filters bound from the query string; all of them are applied in SQL.</summary>
public sealed class ApplicationListQuery
{
    /// <summary>Borrower name, personal number or contract number.</summary>
    public string? Search { get; set; }

    public ApplicationStatus? Status { get; set; }

    public Guid? Product { get; set; }

    public ApplicationSortField Sort { get; set; }

    /// <summary>Ascending order (the default is newest / largest first).</summary>
    public bool Asc { get; set; }

    public int Page { get; set; } = 1;

    public ApplicationSearch ToSearch() => new ApplicationSearch
    {
        Text = Search,
        Status = Status,
        ProductId = Product is { } product ? new LoanProductId(product) : null,
        Sort = Sort,
        Descending = !Asc,
        Page = Page,
    }.Normalize();

    public RouteValueDictionary RouteValues(int? page = null)
    {
        var values = new RouteValueDictionary();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            values["search"] = Search.Trim();
        }

        if (Status is { } status)
        {
            values["status"] = status;
        }

        if (Product is { } product)
        {
            values["product"] = product;
        }

        if (Sort != ApplicationSortField.Updated)
        {
            values["sort"] = Sort;
        }

        if (Asc)
        {
            values["asc"] = true;
        }

        if ((page ?? Page) > 1)
        {
            values["page"] = page ?? Page;
        }

        return values;
    }
}

/// <summary>The application list with its filters and the products for the product filter.</summary>
public sealed record ApplicationIndexViewModel(
    ApplicationListQuery Query, PagedResult<ApplicationListItem> Applications, IReadOnlyList<ProductOption> Products);

/// <summary>A new application: the borrower (found by personal number) and the requested terms.</summary>
public sealed class NewApplicationForm
{
    [Display(Name = "Field.BorrowerPersonalNumber")]
    public string? BorrowerPersonalNumber { get; set; }

    [Display(Name = "Field.Product")]
    public Guid? ProductId { get; set; }

    [Display(Name = "Field.Amount")]
    public decimal Amount { get; set; }

    [Display(Name = "Field.Currency")]
    public Currency Currency { get; set; } = Currency.GEL;

    [Display(Name = "Field.TermMonths")]
    public int TermMonths { get; set; }

    [Display(Name = "Field.AnnualRate")]
    public decimal AnnualRate { get; set; }

    [Display(Name = "Field.ScheduleType")]
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Annuity;
}

/// <summary>The new-application form with the products to choose from.</summary>
public sealed record NewApplicationViewModel(NewApplicationForm Form, IReadOnlyList<ProductOption> Products);

/// <summary>Terms for editing a draft or proposing a counter-offer.</summary>
public sealed class TermsForm
{
    public decimal Amount { get; set; }

    public Currency Currency { get; set; } = Currency.GEL;

    public int TermMonths { get; set; }

    public decimal AnnualRate { get; set; }

    public ScheduleType ScheduleType { get; set; } = ScheduleType.Annuity;

    public string? Reason { get; set; }
}

/// <summary>A co-borrower or guarantor, found by personal number.</summary>
public sealed class PartyForm
{
    public string? PersonalNumber { get; set; }

    public PartyRole Role { get; set; } = PartyRole.CoBorrower;
}

/// <summary>Collateral with its manually entered valuation.</summary>
public sealed class CollateralForm
{
    public CollateralType Type { get; set; } = CollateralType.RealEstate;

    public string? Description { get; set; }

    public decimal MarketValue { get; set; }

    public Currency Currency { get; set; } = Currency.GEL;

    public DateOnly? ValuationDate { get; set; }

    public AddCollateralCommand ToCommand() =>
        new(Type, Description, MarketValue, Currency, ValuationDate ?? DateOnly.MinValue);
}

/// <summary>An underwriting approval: an optional extra condition and, for flagged scoring, the override reason.</summary>
public sealed class ApproveForm
{
    public ConditionType? ConditionType { get; set; }

    public string? ConditionDescription { get; set; }

    public string? OverrideReason { get; set; }

    public IReadOnlyList<RequestedCondition> Conditions() =>
        ConditionType is { } type && !string.IsNullOrWhiteSpace(ConditionDescription)
            ? [new RequestedCondition(type, ConditionDescription)]
            : [];
}

/// <summary>
/// Which action forms the application page shows to the viewer: permission and status, plus the four-eyes rules
/// (never the creator, one decision per person) and the authority tier. The services and the domain enforce the same
/// rules again; this only keeps impossible buttons off the page.
/// </summary>
public sealed record ApplicationActions
{
    public bool EditDraft { get; init; }

    public bool ChangeDocuments { get; init; }

    public bool DownloadDocuments { get; init; }

    public bool Submit { get; init; }

    public bool SendToScoring { get; init; }

    public bool RecordClientResponse { get; init; }

    public bool Cancel { get; init; }

    public bool ReviewCompliance { get; init; }

    public bool Decide { get; init; }

    public bool CounterOffer { get; init; }

    /// <summary>An overridable rule failed: approving needs <c>scoring.override</c> and a reason.</summary>
    public bool NeedsOverride { get; init; }

    public bool Vote { get; init; }

    public bool SignContract { get; init; }

    public bool MarkConditionsMet { get; init; }

    public bool Disburse { get; init; }

    public bool AnyDecision => ReviewCompliance || Decide || CounterOffer || Vote;

    public static ApplicationActions For(UserAccessProfile profile, ApplicationDetails application)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(application);

        var status = application.Summary.Status;
        var me = profile.UserId;
        var independent = application.CreatedBy != me && application.Decisions.All(d => d.DecidedById != me);
        var authority = CurrentAccessProfile.AuthorityOf(profile);
        var mayDecide = status == ApplicationStatus.Underwriting && independent && authority is not null
                        && application.RequiredTier is { } tier && authority.MaxTier >= tier;
        bool Can(string permission) => profile.HasPermission(permission);

        return new ApplicationActions
        {
            EditDraft = Can(Permissions.Applications.Create) && status == ApplicationStatus.Draft,
            ChangeDocuments = Can(Permissions.Documents.Upload)
                              && status is ApplicationStatus.Draft or ApplicationStatus.DocumentsPending or ApplicationStatus.CounterOffered,
            DownloadDocuments = Can(Permissions.Documents.View),
            Submit = Can(Permissions.Applications.Create) && status == ApplicationStatus.Draft,
            SendToScoring = Can(Permissions.Applications.Create) && status == ApplicationStatus.DocumentsPending,
            RecordClientResponse = Can(Permissions.Applications.RecordClientResponse) && status == ApplicationStatus.CounterOffered,
            Cancel = Can(Permissions.Applications.Cancel) && ApplicationStateMachine.Cancellable.Contains(status)
                     && !application.DisbursementRequested,
            ReviewCompliance = Can(Permissions.Compliance.Review) && status == ApplicationStatus.ComplianceReview && independent,
            Decide = mayDecide,
            CounterOffer = mayDecide && Can(Permissions.Underwriting.CounterOffer),
            NeedsOverride = application.LatestScoring?.RequiresOverride == true,
            Vote = Can(Permissions.Committee.Vote) && status == ApplicationStatus.CreditCommittee && independent,
            SignContract = Can(Permissions.Contracts.Manage)
                           && status is ApplicationStatus.Approved or ApplicationStatus.ConditionallyApproved,
            MarkConditionsMet = Can(Permissions.Contracts.Manage) && status == ApplicationStatus.ContractSigned,
            Disburse = Can(Permissions.Loans.Disburse) && status == ApplicationStatus.ConditionsMet,
        };
    }
}

/// <summary>The application page: the data, the actions the viewer may take, and whether they may override scoring.</summary>
public sealed record ApplicationPageViewModel(
    ApplicationDetails Application, ApplicationActions Actions, bool CanOverride, CreditFlow.Domain.Loans.LoanId? Loan);

/// <summary>One decision queue of the viewer and the other queues they may open.</summary>
public sealed record InboxViewModel(Inbox Current, IReadOnlyList<Inbox> Available, PagedResult<ApplicationListItem> Applications);

/// <summary>
/// The loan page: the loan, whether the viewer may record repayments, and an early-repayment quote when one was asked
/// for (<paramref name="QuotedAmount"/> <c>null</c> with a quote = full repayment).
/// </summary>
public sealed record LoanPageViewModel(
    CreditFlow.Application.Disbursements.LoanDetails Loan,
    bool CanRecordRepayment,
    CreditFlow.Domain.Loans.EarlyRepaymentQuote? Quote,
    decimal? QuotedAmount);
