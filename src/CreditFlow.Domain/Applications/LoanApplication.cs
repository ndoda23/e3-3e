using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Calendar;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.Applications;

/// <summary>What <see cref="LoanApplication.Submit"/> needs to know besides the application itself.</summary>
/// <param name="Product">The product with its current version (becomes the snapshot).</param>
/// <param name="Rate">The rate of the loan currency on the submission date (GEL: 1).</param>
/// <param name="Clients">Every party of the application, by id.</param>
/// <param name="BorrowerHasOtherOpenApplication">The borrower already has an open application for this product.</param>
/// <param name="Today">The bank date.</param>
public sealed record SubmissionContext(
    LoanProduct Product,
    ExchangeRate Rate,
    IReadOnlyDictionary<ClientId, Client> Clients,
    bool BorrowerHasOtherOpenApplication,
    DateOnly Today);

/// <summary>What a decider may do, derived from their permissions by the application layer.</summary>
/// <param name="MaxTier">The highest approval tier the decider may decide (Committee members vote instead).</param>
/// <param name="CanOverride">The decider may approve despite failed overridable scoring rules.</param>
public sealed record DeciderAuthority(ApprovalTier MaxTier, bool CanOverride);

/// <summary>
/// A loan application: the consistency boundary of the whole CreditFlow process. Every status transition and
/// invariant lives here; the allowed transitions are <see cref="ApplicationStateMachine"/> and nothing else is
/// possible. Every transition is recorded in <see cref="History"/> (who, when, why) and raised as
/// <see cref="ApplicationStatusChanged"/>.
/// <para>
/// Four-eyes: the creator never decides, and one person takes at most one decision per application over its whole
/// life (compliance, underwriting, counter-offer, committee vote).
/// </para>
/// </summary>
public sealed class LoanApplication : IHasDomainEvents
{
    private readonly List<ApplicationParty> _parties = [];
    private readonly List<Collateral> _collaterals = [];
    private readonly List<ApplicationDocument> _documents = [];
    private readonly List<StatusChange> _history = [];
    private readonly List<ApplicationDecision> _decisions = [];
    private readonly List<ApplicationCondition> _conditions = [];
    private readonly List<ScoringRun> _scoringRuns = [];
    private readonly List<IDomainEvent> _events = [];

    private LoanApplication()
    {
    }

    public LoanApplicationId Id { get; private set; }

    public BranchId BranchId { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public ApplicationStatus Status { get; private set; }

    public DateTimeOffset StatusChangedAt { get; private set; }

    /// <summary>Kept in step with <see cref="Status"/> for the "one open application per client and product" index.</summary>
    public bool IsOpen { get; private set; }

    public LoanProductId ProductId { get; private set; }

    /// <summary>The borrower (also listed in <see cref="Parties"/>).</summary>
    public ClientId BorrowerId { get; private set; }

    public LoanTerms Terms { get; private set; } = null!;

    /// <summary>The product version fixed at submission; <c>null</c> while Draft.</summary>
    public ProductSnapshot? Snapshot { get; private set; }

    /// <summary>The rate used for the GEL equivalent (submission, then scoring).</summary>
    public ExchangeRate? Rate { get; private set; }

    /// <summary>GEL equivalent of the amount (limits, thresholds and the checklist use it).</summary>
    public decimal? AmountGel { get; private set; }

    public ApprovalTier? Tier { get; private set; }

    public DateTimeOffset? EscalatedAt { get; private set; }

    /// <summary>The counter-offered terms awaiting the client's answer.</summary>
    public LoanTerms? ProposedTerms { get; private set; }

    /// <summary>Until when the approval or counter-offer may be accepted.</summary>
    public DateTimeOffset? OfferExpiresAt { get; private set; }

    public string? ContractNumber { get; private set; }

    public DateTimeOffset? ContractSignedAt { get; private set; }

    /// <summary>A disbursement was requested (money may be in flight): cancellation is no longer possible.</summary>
    public bool DisbursementRequested { get; private set; }

    public DateTimeOffset? DisbursedAt { get; private set; }

    public IReadOnlyList<ApplicationParty> Parties => _parties;

    public IReadOnlyList<Collateral> Collaterals => _collaterals;

    public IReadOnlyList<ApplicationDocument> Documents => _documents;

    public IReadOnlyList<StatusChange> History => _history;

    public IReadOnlyList<ApplicationDecision> Decisions => _decisions;

    public IReadOnlyList<ApplicationCondition> Conditions => _conditions;

    public IReadOnlyList<ScoringRun> ScoringRuns => _scoringRuns;

    public ScoringRun? LatestScoring => _scoringRuns.Count == 0 ? null : _scoringRuns[^1];

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    /// <summary>The documents the checklist still needs (product snapshot or current product, by GEL amount).</summary>
    public IReadOnlyList<DocumentType> MissingDocuments(ProductTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var uploaded = _documents.Select(d => d.Type).ToHashSet();
        return terms.DocumentsRequiredFor(AmountGel ?? 0m).Where(t => !uploaded.Contains(t)).ToList();
    }

    public void ClearDomainEvents() => _events.Clear();

    // ----- Draft -----

    public static Result<LoanApplication> Create(
        LoanProduct product, ClientId borrower, LoanTerms terms, BranchId branchId, UserId createdBy, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(terms);

        if (!product.IsActive)
        {
            return Result.Failure<LoanApplication>(ApplicationErrors.ProductInactive);
        }

        var application = new LoanApplication
        {
            Id = LoanApplicationId.New(),
            BranchId = branchId,
            CreatedBy = createdBy,
            CreatedAt = at,
            ProductId = product.Id,
            BorrowerId = borrower,
            Terms = terms,
        };
        application._parties.Add(ApplicationParty.Create(borrower, PartyRole.Borrower));
        application.Record(null, ApplicationStatus.Draft, at, createdBy, null);
        return Result.Success(application);
    }

    public Result ChangeTerms(LoanTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (Status != ApplicationStatus.Draft)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        Terms = terms;
        return Result.Success();
    }

    /// <summary>Adds a co-borrower or guarantor (they go through the same checks and scoring).</summary>
    public Result AddParty(ClientId clientId, PartyRole role)
    {
        if (Status != ApplicationStatus.Draft)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        if (role is not (PartyRole.CoBorrower or PartyRole.Guarantor))
        {
            return Result.Failure(ApplicationErrors.OneBorrower);
        }

        if (_parties.Any(p => p.ClientId == clientId))
        {
            return Result.Failure(ApplicationErrors.DuplicateParty);
        }

        _parties.Add(ApplicationParty.Create(clientId, role));
        return Result.Success();
    }

    public Result RemoveParty(ClientId clientId)
    {
        if (Status != ApplicationStatus.Draft)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        return _parties.RemoveAll(p => p.ClientId == clientId && p.Role != PartyRole.Borrower) > 0
            ? Result.Success()
            : Result.Failure(ApplicationErrors.PartyNotFound);
    }

    /// <summary>Collateral with its manual valuation; editable until scoring starts.</summary>
    public Result<ApplicationLineId> AddCollateral(
        CollateralType type, string? description, Money marketValue, DateOnly valuationDate, DateOnly today)
    {
        if (Status is not (ApplicationStatus.Draft or ApplicationStatus.DocumentsPending))
        {
            return Result.Failure<ApplicationLineId>(ApplicationErrors.WrongStatus(Status));
        }

        var collateral = Collateral.Create(type, description, marketValue, valuationDate, today);
        if (collateral.IsFailure)
        {
            return Result.Failure<ApplicationLineId>(collateral.Error!);
        }

        _collaterals.Add(collateral.Value);
        return Result.Success(collateral.Value.Id);
    }

    public Result RemoveCollateral(ApplicationLineId id)
    {
        if (Status is not (ApplicationStatus.Draft or ApplicationStatus.DocumentsPending))
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        return _collaterals.RemoveAll(c => c.Id == id) > 0
            ? Result.Success()
            : Result.Failure(ApplicationErrors.CollateralNotFound);
    }

    /// <summary>Records an uploaded document (the content is stored by the caller under <paramref name="id"/>).</summary>
    public Result AddDocument(
        ApplicationLineId id, DocumentType type, string fileName, string contentType, long size, string sha256,
        UserId by, DateTimeOffset at)
    {
        if (!CanChangeDocuments)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        _documents.Add(ApplicationDocument.Create(id, type, fileName, contentType, size, sha256, by, at));
        return Result.Success();
    }

    public Result RemoveDocument(ApplicationLineId id)
    {
        if (!CanChangeDocuments)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        return _documents.RemoveAll(d => d.Id == id) > 0
            ? Result.Success()
            : Result.Failure(ApplicationErrors.DocumentNotFound);
    }

    /// <summary>Documents can be added or removed before scoring, and while a counter-offer awaits the client.</summary>
    public bool CanChangeDocuments =>
        Status is ApplicationStatus.Draft or ApplicationStatus.DocumentsPending or ApplicationStatus.CounterOffered;

    // ----- 1. Submission -----

    /// <summary>
    /// Submits the draft: checks the terms against the product's current version, collateral, the age and identity
    /// document of every party and the one-open-application rule (all errors at once), then fixes the product snapshot.
    /// </summary>
    public Result Submit(SubmissionContext context, UserId by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Status != ApplicationStatus.Draft)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        if (context.Product.Id != ProductId)
        {
            throw new ArgumentException("The context holds a different product.", nameof(context));
        }

        var errors = new List<Error>();
        if (!context.Product.IsActive)
        {
            errors.Add(ApplicationErrors.ProductInactive);
        }

        var terms = context.Product.CurrentVersion.Terms;
        var amountGel = context.Rate.ToGel(Terms.Amount).Amount;
        errors.AddRange(CheckTerms(Terms, terms, amountGel));

        if (terms.CollateralRequired && _collaterals.Count == 0)
        {
            errors.Add(ApplicationErrors.CollateralRequired);
        }

        if (context.BorrowerHasOtherOpenApplication)
        {
            errors.Add(ApplicationErrors.OpenApplicationExists);
        }

        errors.AddRange(CheckParties(context.Clients, context.Today, checkMinimumAge: true));

        if (errors.Count > 0)
        {
            return Result.Failure(Error.ValidationFailed(errors));
        }

        Snapshot = ProductSnapshot.Of(context.Product);
        Rate = context.Rate;
        AmountGel = amountGel;
        return MoveTo(ApplicationStatus.Submitted, at, by, null);
    }

    // ----- 2. Pre-check -----

    public Result StartPreCheck(DateTimeOffset at) => MoveTo(ApplicationStatus.PreCheck, at, null, null);

    public Result CompletePreCheck(PreCheckOutcome outcome, string? detail, DateTimeOffset at)
    {
        if (Status != ApplicationStatus.PreCheck)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        return outcome switch
        {
            PreCheckOutcome.Clear => MoveTo(ApplicationStatus.DocumentsPending, at, null, detail),
            PreCheckOutcome.KycFailed => MoveTo(ApplicationStatus.Rejected, at, null, detail ?? "Identity verification failed."),
            PreCheckOutcome.ScreeningMatch => MoveTo(ApplicationStatus.ComplianceReview, at, null, detail ?? "Sanctions or PEP match."),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown pre-check outcome."),
        };
    }

    /// <summary>The compliance officer clears the match or rejects the application (reason required).</summary>
    public Result DecideCompliance(UserId by, bool clear, string? reason, DateTimeOffset at)
    {
        if (Status != ApplicationStatus.ComplianceReview)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        var checks = EnsureCanDecide(by);
        if (checks.IsFailure)
        {
            return checks;
        }

        if (NormalizeReason(reason) is not { } why)
        {
            return Result.Failure(ApplicationErrors.ReasonRequired);
        }

        _decisions.Add(ApplicationDecision.Create(clear ? DecisionKind.ComplianceClear : DecisionKind.ComplianceReject, by, at, why));
        return MoveTo(clear ? ApplicationStatus.DocumentsPending : ApplicationStatus.Rejected, at, by, why);
    }

    // ----- 3. Documents -----

    /// <summary>Starts scoring once the checklist for the product and amount is complete.</summary>
    public Result SendToScoring(UserId by, DateTimeOffset at)
    {
        if (Status != ApplicationStatus.DocumentsPending)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        return MissingDocuments(Snapshot!.Terms).Count > 0
            ? Result.Failure(ApplicationErrors.ChecklistIncomplete)
            : MoveTo(ApplicationStatus.Scoring, at, by, null);
    }

    // ----- 4. Scoring -----

    /// <summary>
    /// Records the scoring run and routes the application: a failed hard rule rejects it automatically; otherwise it
    /// goes to Underwriting or to the Credit Committee according to <paramref name="tier"/>.
    /// </summary>
    /// <param name="run">The explained result of every rule.</param>
    /// <param name="rate">The rate of the scoring date; the GEL amount and the routing use it.</param>
    /// <param name="tier">The approval tier for the GEL amount (from the approval thresholds).</param>
    /// <param name="at">When scoring completed.</param>
    public Result CompleteScoring(ScoringRun run, ExchangeRate rate, ApprovalTier tier, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rate);
        if (Status != ApplicationStatus.Scoring)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        _scoringRuns.Add(run);
        Rate = rate;
        AmountGel = rate.ToGel(Terms.Amount).Amount;
        Tier = tier;
        EscalatedAt = null;

        if (run.HasHardFailure)
        {
            var failed = string.Join(", ", run.Failures.Where(r => !r.Overridable).Select(r => r.RuleCode));
            return MoveTo(ApplicationStatus.Rejected, at, null, $"Scoring rule failed: {failed}.");
        }

        return tier == ApprovalTier.Committee
            ? MoveTo(ApplicationStatus.CreditCommittee, at, null, null)
            : MoveTo(ApplicationStatus.Underwriting, at, null, null);
    }

    // ----- 5. Underwriting -----

    /// <summary>The tier needed to decide now: escalated files need at least a Senior Underwriter.</summary>
    public ApprovalTier? RequiredTier =>
        Tier is { } tier && EscalatedAt is not null && tier < ApprovalTier.SeniorUnderwriter
            ? ApprovalTier.SeniorUnderwriter
            : Tier;

    /// <summary>
    /// Approves. With <paramref name="conditions"/> the result is ConditionallyApproved, otherwise Approved; the
    /// product's standard conditions are added in both cases. The offer is valid for 30 days.
    /// </summary>
    public Result Approve(
        UserId by, DeciderAuthority authority, IReadOnlyList<RequestedCondition> conditions, string? overrideReason,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        var checks = EnsureUnderwriterCanDecide(by, authority);
        if (checks.IsFailure)
        {
            return checks;
        }

        string? why = null;
        if (LatestScoring?.RequiresOverride == true
            && (!authority.CanOverride || (why = NormalizeReason(overrideReason)) is null))
        {
            return Result.Failure(ApplicationErrors.OverrideReasonRequired);
        }

        var requested = new List<ApplicationCondition>();
        foreach (var condition in conditions)
        {
            if (!Enum.IsDefined(condition.Type)
                || Text.Normalize(condition.Description) is not { Length: > 0 and <= ApplicationCondition.DescriptionMaxLength } text)
            {
                return Result.Failure(ApplicationErrors.InvalidCondition);
            }

            requested.Add(ApplicationCondition.Create(condition.Type, text));
        }

        _decisions.Add(ApplicationDecision.Create(DecisionKind.Approve, by, at, null, why));
        return CompleteApproval(requested, by, at, why);
    }

    public Result Reject(UserId by, DeciderAuthority authority, string? reason, DateTimeOffset at)
    {
        var checks = EnsureUnderwriterCanDecide(by, authority);
        if (checks.IsFailure)
        {
            return checks;
        }

        if (NormalizeReason(reason) is not { } why)
        {
            return Result.Failure(ApplicationErrors.ReasonRequired);
        }

        _decisions.Add(ApplicationDecision.Create(DecisionKind.Reject, by, at, why));
        return MoveTo(ApplicationStatus.Rejected, at, by, why);
    }

    /// <summary>Offers a different amount, term or rate (same currency and schedule type, within the snapshot).</summary>
    public Result CounterOffer(UserId by, DeciderAuthority authority, LoanTerms terms, string? reason, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var checks = EnsureUnderwriterCanDecide(by, authority);
        if (checks.IsFailure)
        {
            return checks;
        }

        var errors = CheckTerms(terms, Snapshot!.Terms, Rate!.ToGel(terms.Amount).Amount).ToList();
        if (terms.Amount.Currency != Terms.Amount.Currency)
        {
            errors.Add(ApplicationErrors.CurrencyNotOffered);
        }

        if (errors.Count > 0)
        {
            return Result.Failure(Error.ValidationFailed(errors));
        }

        var why = NormalizeReason(reason);
        _decisions.Add(ApplicationDecision.Create(DecisionKind.CounterOffer, by, at, why));
        ProposedTerms = terms;
        OfferExpiresAt = at.AddDays(LendingPolicy.OfferValidityDays);
        return MoveTo(ApplicationStatus.CounterOffered, at, by, why);
    }

    /// <summary>The client accepted the counter-offer: the new terms replace the old ones and scoring runs again.</summary>
    public Result AcceptCounterOffer(UserId by, DateTimeOffset at)
    {
        if (Status != ApplicationStatus.CounterOffered || ProposedTerms is null)
        {
            return Result.Failure(ApplicationErrors.NoPendingOffer);
        }

        if (at >= OfferExpiresAt)
        {
            return Result.Failure(ApplicationErrors.OfferExpired);
        }

        var previous = (Terms, AmountGel);
        Terms = ProposedTerms;
        AmountGel = Rate!.ToGel(Terms.Amount).Amount;
        if (MissingDocuments(Snapshot!.Terms).Count > 0)
        {
            (Terms, AmountGel) = previous;
            return Result.Failure(ApplicationErrors.ChecklistIncomplete);
        }

        ProposedTerms = null;
        OfferExpiresAt = null;
        return MoveTo(ApplicationStatus.Scoring, at, by, "Client accepted the counter-offer.");
    }

    public Result DeclineCounterOffer(UserId by, string? reason, DateTimeOffset at)
    {
        if (Status != ApplicationStatus.CounterOffered)
        {
            return Result.Failure(ApplicationErrors.NoPendingOffer);
        }

        if (NormalizeReason(reason) is not { } why)
        {
            return Result.Failure(ApplicationErrors.ReasonRequired);
        }

        ProposedTerms = null;
        OfferExpiresAt = null;
        return MoveTo(ApplicationStatus.Cancelled, at, by, why);
    }

    // ----- 5. Credit Committee -----

    /// <summary>
    /// A committee member votes (once). When <paramref name="requiredVotes"/> votes agree, the application is approved
    /// (with the product's standard conditions) or rejected.
    /// </summary>
    public Result Vote(UserId by, bool approve, string? reason, int requiredVotes, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredVotes, 1);
        if (Status != ApplicationStatus.CreditCommittee)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        var checks = EnsureCanDecide(by);
        if (checks.IsFailure)
        {
            return checks;
        }

        var why = NormalizeReason(reason);
        if (!approve && why is null)
        {
            return Result.Failure(ApplicationErrors.ReasonRequired);
        }

        var kind = approve ? DecisionKind.VoteApprove : DecisionKind.VoteReject;
        _decisions.Add(ApplicationDecision.Create(kind, by, at, why));

        if (_decisions.Count(d => d.Kind == kind) < requiredVotes)
        {
            return Result.Success();
        }

        return approve
            ? CompleteApproval([], by, at, "Approved by the credit committee.")
            : MoveTo(ApplicationStatus.Rejected, at, by, $"Rejected by the credit committee: {why}");
    }

    // ----- 6. Offer expiry -----

    /// <summary>Expires an approval or counter-offer the client did not accept in time (background job).</summary>
    public Result Expire(DateTimeOffset at)
    {
        if (Status is not (ApplicationStatus.CounterOffered or ApplicationStatus.Approved or ApplicationStatus.ConditionallyApproved)
            || OfferExpiresAt is not { } expiresAt || at < expiresAt)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        ProposedTerms = null;
        return MoveTo(ApplicationStatus.Expired, at, null, $"The offer was not accepted within {LendingPolicy.OfferValidityDays} days.");
    }

    // ----- Escalation -----

    /// <summary>When an application that entered Underwriting at <see cref="StatusChangedAt"/> must be escalated.</summary>
    public DateTimeOffset? EscalationDeadline(WorkingDayCalendar calendar, int workingDays)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return Status == ApplicationStatus.Underwriting && EscalatedAt is null
            ? calendar.AddWorkingDays(StatusChangedAt, workingDays)
            : null;
    }

    /// <summary>Escalates to a Senior Underwriter (idempotent: a second call changes nothing).</summary>
    public Result Escalate(DateTimeOffset at)
    {
        if (Status != ApplicationStatus.Underwriting)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        if (EscalatedAt is null)
        {
            EscalatedAt = at;
            _events.Add(new ApplicationEscalated(Id, at));
        }

        return Result.Success();
    }

    // ----- 7. Contract -----

    /// <summary>The client signed the contract within the offer's validity; the contract number is assigned.</summary>
    public Result SignContract(UserId by, string contractNumber, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractNumber);
        if (Status is not (ApplicationStatus.Approved or ApplicationStatus.ConditionallyApproved))
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        if (at >= OfferExpiresAt)
        {
            return Result.Failure(ApplicationErrors.OfferExpired);
        }

        ContractNumber = contractNumber;
        ContractSignedAt = at;
        var signed = MoveTo(ApplicationStatus.ContractSigned, at, by, contractNumber);
        return signed.IsSuccess && _conditions.All(c => c.IsMet)
            ? MoveTo(ApplicationStatus.ConditionsMet, at, by, "No conditions to meet.")
            : signed;
    }

    public Result MarkConditionMet(UserId by, ApplicationLineId conditionId, DateTimeOffset at)
    {
        if (Status != ApplicationStatus.ContractSigned)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        var condition = _conditions.SingleOrDefault(c => c.Id == conditionId);
        if (condition is null)
        {
            return Result.Failure(ApplicationErrors.ConditionNotFound);
        }

        if (!condition.IsMet)
        {
            condition.MarkMet(by, at);
        }

        return _conditions.All(c => c.IsMet)
            ? MoveTo(ApplicationStatus.ConditionsMet, at, by, "All conditions met.")
            : Result.Success();
    }

    // ----- 8. Disbursement -----

    /// <summary>
    /// Marks that a disbursement was requested, after checking that every party's identity document is valid today
    /// and that the age at maturity is still within the policy. From now on the application cannot be cancelled.
    /// </summary>
    public Result RequestDisbursement(IReadOnlyDictionary<ClientId, Client> clients, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(clients);
        if (Status != ApplicationStatus.ConditionsMet)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        var errors = CheckParties(clients, today, checkMinimumAge: false).ToList();
        if (errors.Count > 0)
        {
            return Result.Failure(Error.ValidationFailed(errors));
        }

        DisbursementRequested = true;
        return Result.Success();
    }

    /// <summary>Core banking reported a definite failure (no money moved): the request may be retried or cancelled.</summary>
    public Result DisbursementFailed()
    {
        if (Status != ApplicationStatus.ConditionsMet || !DisbursementRequested)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        DisbursementRequested = false;
        return Result.Success();
    }

    public Result MarkDisbursed(DateTimeOffset at)
    {
        if (!DisbursementRequested)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        DisbursedAt = at;
        return MoveTo(ApplicationStatus.Disbursed, at, null, null);
    }

    /// <summary>The loan and its schedule were booked.</summary>
    public Result Activate(DateTimeOffset at) => MoveTo(ApplicationStatus.Active, at, null, null);

    /// <summary>The loan was repaid in full.</summary>
    public Result Close(DateTimeOffset at) => MoveTo(ApplicationStatus.Closed, at, null, "Repaid in full.");

    // ----- Cancellation -----

    /// <summary>Cancels at any time before disbursement (reason required).</summary>
    public Result Cancel(UserId by, string? reason, DateTimeOffset at)
    {
        if (DisbursementRequested)
        {
            return Result.Failure(ApplicationErrors.DisbursementInProgress);
        }

        if (NormalizeReason(reason) is not { } why)
        {
            return Result.Failure(ApplicationErrors.ReasonRequired);
        }

        ProposedTerms = null;
        return MoveTo(ApplicationStatus.Cancelled, at, by, why);
    }

    // ----- Rules -----

    private Result EnsureCanDecide(UserId by)
    {
        if (by == CreatedBy)
        {
            return Result.Failure(ApplicationErrors.CreatorCannotDecide);
        }

        return _decisions.Any(d => d.DecidedBy == by)
            ? Result.Failure(ApplicationErrors.AlreadyDecided)
            : Result.Success();
    }

    private Result EnsureUnderwriterCanDecide(UserId by, DeciderAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (Status != ApplicationStatus.Underwriting)
        {
            return Result.Failure(ApplicationErrors.WrongStatus(Status));
        }

        var fourEyes = EnsureCanDecide(by);
        if (fourEyes.IsFailure)
        {
            return fourEyes;
        }

        return RequiredTier is { } required && authority.MaxTier >= required
            ? Result.Success()
            : Result.Failure(ApplicationErrors.AuthorityTooLow);
    }

    private Result CompleteApproval(List<ApplicationCondition> requested, UserId by, DateTimeOffset at, string? reason)
    {
        foreach (var type in Snapshot!.Terms.StandardConditions)
        {
            _conditions.Add(ApplicationCondition.Create(type, $"Standard condition of {Snapshot.Name}: {type}."));
        }

        _conditions.AddRange(requested);
        OfferExpiresAt = at.AddDays(LendingPolicy.OfferValidityDays);
        return MoveTo(
            requested.Count > 0 ? ApplicationStatus.ConditionallyApproved : ApplicationStatus.Approved, at, by, reason);
    }

    private static IEnumerable<Error> CheckTerms(LoanTerms requested, ProductTerms product, decimal amountGel)
    {
        if (!product.Currencies.Contains(requested.Amount.Currency))
        {
            yield return ApplicationErrors.CurrencyNotOffered;
        }

        if (!product.ScheduleTypes.Contains(requested.ScheduleType))
        {
            yield return ApplicationErrors.ScheduleTypeNotOffered;
        }

        if (!product.AllowsAmount(amountGel))
        {
            yield return ApplicationErrors.AmountOutOfRange;
        }

        if (!product.AllowsTerm(requested.TermMonths))
        {
            yield return ApplicationErrors.TermOutOfRange;
        }

        if (!product.AllowsRate(requested.AnnualRate))
        {
            yield return ApplicationErrors.RateOutOfRange;
        }
    }

    /// <summary>
    /// For every party: the identity document is valid today and the age at maturity is within the policy; at
    /// submission also the minimum age at application.
    /// </summary>
    private IEnumerable<Error> CheckParties(
        IReadOnlyDictionary<ClientId, Client> clients, DateOnly today, bool checkMinimumAge)
    {
        var maturity = today.AddMonths(Terms.TermMonths);
        foreach (var party in _parties)
        {
            if (!clients.TryGetValue(party.ClientId, out var client))
            {
                throw new ArgumentException($"Client {party.ClientId} of the application was not provided.", nameof(clients));
            }

            if (checkMinimumAge && client.AgeOn(today) < LendingPolicy.MinAgeAtApplication)
            {
                yield return ApplicationErrors.PartyTooYoung(client.FullName);
            }

            if (client.AgeOn(maturity) > LendingPolicy.MaxAgeAtMaturity)
            {
                yield return ApplicationErrors.PartyTooOldAtMaturity(client.FullName);
            }

            if (!client.IdentityDocument.IsValidOn(today))
            {
                yield return ApplicationErrors.IdentityDocumentInvalid(client.FullName);
            }
        }
    }

    private Result MoveTo(ApplicationStatus to, DateTimeOffset at, UserId? by, string? reason)
    {
        if (!ApplicationStateMachine.CanTransition(Status, to))
        {
            return Result.Failure(ApplicationErrors.InvalidTransition(Status, to));
        }

        Record(Status, to, at, by, reason);
        return Result.Success();
    }

    private void Record(ApplicationStatus? from, ApplicationStatus to, DateTimeOffset at, UserId? by, string? reason)
    {
        Status = to;
        StatusChangedAt = at;
        IsOpen = ApplicationStateMachine.Open.Contains(to);
        _history.Add(StatusChange.Create(from, to, at, by, reason));
        _events.Add(new ApplicationStatusChanged(Id, from, to, at, by, reason));
    }

    private static string? NormalizeReason(string? reason) =>
        Text.Normalize(reason) is { Length: > 0 and <= StatusChange.ReasonMaxLength } text ? text : null;
}
