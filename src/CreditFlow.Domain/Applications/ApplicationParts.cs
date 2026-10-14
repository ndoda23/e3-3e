using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.Applications;

/// <summary>Identifier of a loan application.</summary>
public readonly record struct LoanApplicationId(Guid Value)
{
    public static LoanApplicationId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Identifier of a line inside an application (collateral, document, condition, decision).</summary>
public readonly record struct ApplicationLineId(Guid Value)
{
    public static ApplicationLineId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Who may decide an application, by amount (GEL equivalent).</summary>
public enum ApprovalTier
{
    Underwriter = 1,
    SeniorUnderwriter = 2,
    Committee = 3,
}

public enum PartyRole
{
    Borrower = 1,
    CoBorrower = 2,
    Guarantor = 3,
}

public enum CollateralType
{
    RealEstate = 1,
    Vehicle = 2,
}

/// <summary>Outcome of the pre-check (KYC, sanctions / PEP, bureau) over all parties.</summary>
public enum PreCheckOutcome
{
    /// <summary>Every party verified and clear; bureau reports received.</summary>
    Clear = 1,

    /// <summary>A party's identity could not be verified.</summary>
    KycFailed = 2,

    /// <summary>A party matched a sanctions or PEP list: compliance must decide.</summary>
    ScreeningMatch = 3,
}

public enum DecisionKind
{
    ComplianceClear = 1,
    ComplianceReject = 2,
    Approve = 3,
    Reject = 4,
    CounterOffer = 5,
    VoteApprove = 6,
    VoteReject = 7,
}

/// <summary>The requested (or counter-offered) loan terms.</summary>
public sealed record LoanTerms
{
    /// <summary>For materialization by the persistence layer only.</summary>
    private LoanTerms()
    {
    }

    private LoanTerms(Money amount, int termMonths, decimal annualRate, ScheduleType scheduleType)
    {
        Amount = amount;
        TermMonths = termMonths;
        AnnualRate = annualRate;
        ScheduleType = scheduleType;
    }

    public Money Amount { get; }

    public int TermMonths { get; }

    /// <summary>Annual interest rate, percent.</summary>
    public decimal AnnualRate { get; }

    public ScheduleType ScheduleType { get; }

    public static Result<LoanTerms> Create(Money amount, int termMonths, decimal annualRate, ScheduleType scheduleType)
    {
        var errors = new List<Error>();
        if (!amount.IsPositive)
        {
            errors.Add(ApplicationErrors.InvalidAmount);
        }

        if (termMonths < 1)
        {
            errors.Add(ApplicationErrors.InvalidTerm);
        }

        if (annualRate is < 0m or > ProductTerms.MaxPercent)
        {
            errors.Add(ApplicationErrors.InvalidRate);
        }

        if (!Enum.IsDefined(scheduleType))
        {
            errors.Add(ApplicationErrors.InvalidScheduleType);
        }

        return errors.Count == 0
            ? Result.Success(new LoanTerms(amount, termMonths, annualRate, scheduleType))
            : Result.Failure<LoanTerms>(Error.ValidationFailed(errors));
    }
}

/// <summary>The product version an application was submitted under; later product changes do not affect it.</summary>
public sealed record ProductSnapshot(
    LoanProductId ProductId, string Code, string Name, ProductType Type, int Version, ProductTerms Terms)
{
    public static ProductSnapshot Of(LoanProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);
        var version = product.CurrentVersion;
        return new(product.Id, product.Code, product.Name, product.Type, version.Number, version.Terms);
    }
}

/// <summary>A client on the application. Every party goes through the same KYC, screening and scoring.</summary>
public sealed class ApplicationParty
{
    private ApplicationParty()
    {
    }

    public ClientId ClientId { get; private set; }

    public PartyRole Role { get; private set; }

    internal static ApplicationParty Create(ClientId clientId, PartyRole role) => new() { ClientId = clientId, Role = role };
}

/// <summary>Collateral with its manual valuation (value and date entered by the operator).</summary>
public sealed class Collateral
{
    public const int DescriptionMaxLength = 300;

    private Collateral()
    {
    }

    public ApplicationLineId Id { get; private set; }

    public CollateralType Type { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public Money MarketValue { get; private set; }

    public DateOnly ValuationDate { get; private set; }

    internal static Result<Collateral> Create(
        CollateralType type, string? description, Money marketValue, DateOnly valuationDate, DateOnly today)
    {
        var normalized = Text.Normalize(description);
        if (!Enum.IsDefined(type) || normalized.Length is 0 or > DescriptionMaxLength)
        {
            return Result.Failure<Collateral>(ApplicationErrors.InvalidCollateral);
        }

        if (!marketValue.IsPositive || valuationDate > today)
        {
            return Result.Failure<Collateral>(ApplicationErrors.InvalidValuation);
        }

        return Result.Success(new Collateral
        {
            Id = ApplicationLineId.New(), Type = type, Description = normalized, MarketValue = marketValue,
            ValuationDate = valuationDate,
        });
    }
}

/// <summary>Metadata of an uploaded document; the content is stored separately.</summary>
public sealed class ApplicationDocument
{
    public const int FileNameMaxLength = 255;

    private ApplicationDocument()
    {
    }

    public ApplicationLineId Id { get; private set; }

    public DocumentType Type { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long Size { get; private set; }

    /// <summary>SHA-256 of the content, hex.</summary>
    public string Sha256 { get; private set; } = string.Empty;

    public UserId UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    internal static ApplicationDocument Create(
        ApplicationLineId id, DocumentType type, string fileName, string contentType, long size, string sha256,
        UserId by, DateTimeOffset at) =>
        new()
        {
            Id = id, Type = type, FileName = fileName, ContentType = contentType, Size = size, Sha256 = sha256,
            UploadedBy = by, UploadedAt = at,
        };
}

/// <summary>One step in the status history: who moved the application, when and why.</summary>
public sealed class StatusChange
{
    public const int ReasonMaxLength = 1000;

    private StatusChange()
    {
    }

    public long Id { get; private set; }

    public ApplicationStatus? From { get; private set; }

    public ApplicationStatus To { get; private set; }

    public DateTimeOffset At { get; private set; }

    /// <summary><c>null</c> when the system made the change (pre-check, scoring, jobs).</summary>
    public UserId? By { get; private set; }

    public string? Reason { get; private set; }

    internal static StatusChange Create(
        ApplicationStatus? from, ApplicationStatus to, DateTimeOffset at, UserId? by, string? reason) =>
        new() { From = from, To = to, At = at, By = by, Reason = reason };
}

/// <summary>
/// A decision taken by a person: compliance, underwriting, counter-offer or committee vote. A person takes at most
/// one decision per application (four-eyes), enforced here and by a unique index.
/// </summary>
public sealed class ApplicationDecision
{
    private ApplicationDecision()
    {
    }

    public ApplicationLineId Id { get; private set; }

    public DecisionKind Kind { get; private set; }

    public UserId DecidedBy { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>Reason for overriding failed overridable scoring rules, when the decision approves despite them.</summary>
    public string? OverrideReason { get; private set; }

    internal static ApplicationDecision Create(
        DecisionKind kind, UserId by, DateTimeOffset at, string? reason, string? overrideReason = null) =>
        new()
        {
            Id = ApplicationLineId.New(), Kind = kind, DecidedBy = by, DecidedAt = at, Reason = reason,
            OverrideReason = overrideReason,
        };
}

/// <summary>A condition to meet between contract signing and disbursement.</summary>
public sealed class ApplicationCondition
{
    public const int DescriptionMaxLength = 300;

    private ApplicationCondition()
    {
    }

    public ApplicationLineId Id { get; private set; }

    public ConditionType Type { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public bool IsMet { get; private set; }

    public DateTimeOffset? MetAt { get; private set; }

    public UserId? MetBy { get; private set; }

    internal static ApplicationCondition Create(ConditionType type, string description) =>
        new() { Id = ApplicationLineId.New(), Type = type, Description = description };

    internal void MarkMet(UserId by, DateTimeOffset at)
    {
        IsMet = true;
        MetBy = by;
        MetAt = at;
    }
}

/// <summary>A condition requested by a decider (product-standard conditions are added automatically).</summary>
public sealed record RequestedCondition(ConditionType Type, string Description);

/// <summary>The status of an application changed.</summary>
public sealed record ApplicationStatusChanged(
    LoanApplicationId ApplicationId, ApplicationStatus? From, ApplicationStatus To, DateTimeOffset OccurredAt,
    UserId? By, string? Reason) : IDomainEvent;

/// <summary>The application stayed too long in Underwriting and now needs a Senior Underwriter.</summary>
public sealed record ApplicationEscalated(LoanApplicationId ApplicationId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>A scoring run completed (audited with every rule result).</summary>
public sealed record ApplicationScored(LoanApplicationId ApplicationId, Scoring.ScoringRun Run, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>A person took a decision (audited: approvals, rejections, overrides, counter-offers, votes, compliance).</summary>
public sealed record ApplicationDecisionRecorded(
    LoanApplicationId ApplicationId, DecisionKind Kind, UserId DecidedBy, string? Reason, string? OverrideReason,
    DateTimeOffset OccurredAt) : IDomainEvent;
