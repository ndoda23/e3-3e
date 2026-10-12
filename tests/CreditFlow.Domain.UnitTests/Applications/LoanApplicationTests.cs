using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Calendar;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using static CreditFlow.Domain.UnitTests.Applications.ApplicationFixture;

namespace CreditFlow.Domain.UnitTests.Applications;

public sealed class LoanApplicationTests
{
    private readonly ApplicationFixture _f = new();

    // ----- Draft and submission -----

    [Fact]
    public void A_new_application_is_a_draft_with_the_borrower_and_one_history_entry()
    {
        var application = _f.Draft();

        Assert.Equal(ApplicationStatus.Draft, application.Status);
        Assert.True(application.IsOpen);
        Assert.Equal(PartyRole.Borrower, Assert.Single(application.Parties).Role);
        var created = Assert.Single(application.History);
        Assert.Null(created.From);
        Assert.Equal(_f.Operator, created.By);
        Assert.Single(application.DomainEvents.OfType<ApplicationStatusChanged>());
    }

    [Fact]
    public void Submission_fixes_the_product_snapshot_so_later_versions_do_not_apply()
    {
        var application = _f.Draft();
        Assert.True(application.Submit(_f.Context(), _f.Operator, Now).IsSuccess);

        _f.Product.PublishVersion(ConsumerTerms() with { MaxRate = 15m }, Now.AddDays(1));

        Assert.Equal(1, application.Snapshot!.Version);
        Assert.Equal(24m, application.Snapshot.Terms.MaxRate);
        Assert.Equal(10_000m, application.AmountGel);
    }

    [Fact]
    public void Submission_reports_every_problem_at_once()
    {
        var minor = NewClient("01001011003", Today.AddYears(-17));
        var old = NewClient("01001011004", Today.AddYears(-72), documentExpiry: Today.AddDays(-1));
        var application = _f.Draft(LoanTerms.Create(Money.Of(100m, Currency.EUR), 61, 30m, ScheduleType.Differentiated).Value);
        application.AddParty(minor.Id, PartyRole.CoBorrower);
        application.AddParty(old.Id, PartyRole.Guarantor);
        var clients = new Dictionary<ClientId, Client> { [_f.Borrower.Id] = _f.Borrower, [minor.Id] = minor, [old.Id] = old };
        var eur = new ExchangeRate(Currency.EUR, 3m, Today);

        var result = application.Submit(new SubmissionContext(_f.Product, eur, clients, true, Today), _f.Operator, Now);

        Assert.Equal(
            [
                "Application.CurrencyNotOffered", "Application.ScheduleTypeNotOffered", "Application.AmountOutOfRange",
                "Application.TermOutOfRange", "Application.RateOutOfRange", "Application.OpenApplicationExists",
                "Application.PartyTooYoung", "Application.PartyTooOldAtMaturity", "Application.IdentityDocumentInvalid",
            ],
            result.Error!.Details.Select(e => e.Code));
        Assert.Equal(ApplicationStatus.Draft, application.Status);
    }

    [Fact]
    public void A_party_turning_76_before_the_last_installment_is_refused()
    {
        // 70 years and 11 months old: with a 60-month term they would be 75 at the end (allowed); 61 months not.
        var client = NewClient("01001011005", Today.AddYears(-71).AddMonths(1));
        var clients = new Dictionary<ClientId, Client> { [client.Id] = client };
        var allowed = LoanApplication.Create(_f.Product, client.Id, Terms(months: 60), _f.Branch, _f.Operator, Now).Value;

        Assert.True(allowed.Submit(new SubmissionContext(_f.Product, ExchangeRate.Gel(Today), clients, false, Today), _f.Operator, Now).IsSuccess);
        Assert.Equal(75, client.AgeOn(Today.AddMonths(60)));
    }

    [Fact]
    public void A_foreign_currency_amount_is_checked_by_its_GEL_equivalent()
    {
        var application = _f.Draft(Terms(80_000m, currency: Currency.USD)); // 216,000 GEL > 200,000

        var result = application.Submit(_f.Context(rate: new ExchangeRate(Currency.USD, 2.7m, Today)), _f.Operator, Now);

        Assert.Equal(ApplicationErrors.AmountOutOfRange, result.Error);
    }

    [Fact]
    public void Collateral_is_required_for_secured_products()
    {
        var mortgage = new ApplicationFixture(ConsumerTerms() with { CollateralRequired = true, MaxLtvPercent = 80m }, ProductType.Mortgage);
        var application = mortgage.Draft();

        Assert.Equal(ApplicationErrors.CollateralRequired, application.Submit(mortgage.Context(), mortgage.Operator, Now).Error);

        application.AddCollateral(CollateralType.RealEstate, "Flat in Vake", Money.Of(50_000m, Currency.GEL), Today.AddMonths(-1), Today);
        Assert.True(application.Submit(mortgage.Context(), mortgage.Operator, Now).IsSuccess);
    }

    [Fact]
    public void Drafts_are_edited_only_while_draft()
    {
        var application = _f.In(ApplicationStatus.Submitted);

        Assert.Equal("Application.WrongStatus", application.ChangeTerms(Terms()).Error?.Code);
        Assert.Equal("Application.WrongStatus", application.AddParty(_f.CoBorrower.Id, PartyRole.Guarantor).Error?.Code);
    }

    [Fact]
    public void Parties_are_unique_and_there_is_one_borrower()
    {
        var application = _f.Draft();

        Assert.Equal(ApplicationErrors.DuplicateParty, application.AddParty(_f.Borrower.Id, PartyRole.Guarantor).Error);
        Assert.Equal(ApplicationErrors.OneBorrower, application.AddParty(_f.CoBorrower.Id, PartyRole.Borrower).Error);
        Assert.True(application.AddParty(_f.CoBorrower.Id, PartyRole.CoBorrower).IsSuccess);
        Assert.Equal(ApplicationErrors.PartyNotFound, application.RemoveParty(_f.Borrower.Id).Error);
    }

    // ----- Pre-check and compliance -----

    [Theory]
    [InlineData(PreCheckOutcome.Clear, ApplicationStatus.DocumentsPending)]
    [InlineData(PreCheckOutcome.KycFailed, ApplicationStatus.Rejected)]
    [InlineData(PreCheckOutcome.ScreeningMatch, ApplicationStatus.ComplianceReview)]
    public void Pre_check_routes_by_outcome(PreCheckOutcome outcome, ApplicationStatus expected)
    {
        var application = _f.In(ApplicationStatus.PreCheck);

        Assert.True(application.CompletePreCheck(outcome, null, Now).IsSuccess);

        Assert.Equal(expected, application.Status);
        Assert.Null(application.History[^1].By);
    }

    [Fact]
    public void Compliance_decides_with_a_reason_and_never_on_own_application()
    {
        var application = _f.In(ApplicationStatus.PreCheck);
        application.CompletePreCheck(PreCheckOutcome.ScreeningMatch, "PEP match", Now);

        Assert.Equal(ApplicationErrors.CreatorCannotDecide, application.DecideCompliance(_f.Operator, true, "ok", Now).Error);
        Assert.Equal(ApplicationErrors.ReasonRequired, application.DecideCompliance(_f.Compliance, true, " ", Now).Error);
        Assert.True(application.DecideCompliance(_f.Compliance, true, "Name match only, different person.", Now).IsSuccess);

        Assert.Equal(ApplicationStatus.DocumentsPending, application.Status);
        Assert.Equal(DecisionKind.ComplianceClear, Assert.Single(application.Decisions).Kind);
    }

    // ----- Documents and scoring -----

    [Fact]
    public void Scoring_needs_the_complete_checklist_for_the_amount()
    {
        var application = _f.In(ApplicationStatus.DocumentsPending);
        _f.UploadChecklist(application, DocumentType.IdentityDocumentCopy);

        Assert.Equal([DocumentType.IncomeStatement], application.MissingDocuments(application.Snapshot!.Terms));
        Assert.Equal(ApplicationErrors.ChecklistIncomplete, application.SendToScoring(_f.Operator, Now).Error);

        _f.UploadChecklist(application, DocumentType.IncomeStatement);
        Assert.True(application.SendToScoring(_f.Operator, Now).IsSuccess);
    }

    [Fact]
    public void Larger_amounts_need_more_documents()
    {
        var application = _f.Draft(Terms(25_000m));
        application.Submit(_f.Context(), _f.Operator, Now);

        Assert.Contains(DocumentType.EmploymentCertificate, application.MissingDocuments(application.Snapshot!.Terms));
    }

    [Theory]
    [InlineData(ApprovalTier.Underwriter, ApplicationStatus.Underwriting)]
    [InlineData(ApprovalTier.SeniorUnderwriter, ApplicationStatus.Underwriting)]
    [InlineData(ApprovalTier.Committee, ApplicationStatus.CreditCommittee)]
    public void Scoring_routes_by_approval_tier(ApprovalTier tier, ApplicationStatus expected)
    {
        var application = _f.In(ApplicationStatus.Scoring);

        application.CompleteScoring(Passing(), ExchangeRate.Gel(Today), tier, Now);

        Assert.Equal(expected, application.Status);
        Assert.Equal(tier, application.Tier);
    }

    [Fact]
    public void A_failed_hard_rule_rejects_automatically_with_the_rule_as_reason()
    {
        var application = _f.In(ApplicationStatus.Scoring);

        application.CompleteScoring(HardFailure(), ExchangeRate.Gel(Today), ApprovalTier.Underwriter, Now);

        Assert.Equal(ApplicationStatus.Rejected, application.Status);
        Assert.Contains("BUREAU_DPD30_12M", application.History[^1].Reason, StringComparison.Ordinal);
        Assert.Single(application.ScoringRuns);
    }

    // ----- Underwriting: four-eyes, authority, override -----

    [Fact]
    public void The_creator_can_never_approve()
    {
        var application = _f.In(ApplicationStatus.Underwriting);

        Assert.Equal(ApplicationErrors.CreatorCannotDecide,
            application.Approve(_f.Operator, SeniorAuthority, [], null, Now).Error);
    }

    [Fact]
    public void An_underwriter_cannot_decide_above_their_tier()
    {
        var application = _f.In(ApplicationStatus.Scoring);
        application.CompleteScoring(Passing(), ExchangeRate.Gel(Today), ApprovalTier.SeniorUnderwriter, Now);

        Assert.Equal(ApplicationErrors.AuthorityTooLow, application.Approve(_f.Underwriter, UnderwriterAuthority, [], null, Now).Error);
        Assert.True(application.Approve(_f.Senior, SeniorAuthority, [], null, Now).IsSuccess);
    }

    [Fact]
    public void Approval_without_conditions_is_Approved_with_standard_conditions_and_a_30_day_offer()
    {
        var application = _f.In(ApplicationStatus.Underwriting);

        application.Approve(_f.Underwriter, UnderwriterAuthority, [], null, Now);

        Assert.Equal(ApplicationStatus.Approved, application.Status);
        Assert.Equal(ConditionType.LifeInsurance, Assert.Single(application.Conditions).Type);
        Assert.Equal(Now.AddDays(30), application.OfferExpiresAt);
    }

    [Fact]
    public void Approval_with_conditions_is_ConditionallyApproved()
    {
        var application = _f.In(ApplicationStatus.Underwriting);

        application.Approve(
            _f.Underwriter, UnderwriterAuthority, [new RequestedCondition(ConditionType.AdditionalDocument, "Latest payslip")], null, Now);

        Assert.Equal(ApplicationStatus.ConditionallyApproved, application.Status);
        Assert.Equal(2, application.Conditions.Count);
    }

    [Fact]
    public void Failed_overridable_rules_need_the_override_permission_and_a_reason()
    {
        var application = _f.In(ApplicationStatus.Scoring);
        application.CompleteScoring(OverridableFailure(), ExchangeRate.Gel(Today), ApprovalTier.Underwriter, Now);

        Assert.Equal(ApplicationErrors.OverrideReasonRequired, application.Approve(_f.Underwriter, UnderwriterAuthority, [], "ok", Now).Error);
        Assert.Equal(ApplicationErrors.OverrideReasonRequired, application.Approve(_f.Senior, SeniorAuthority, [], null, Now).Error);
        Assert.True(application.Approve(_f.Senior, SeniorAuthority, [], "Stable income, low LTV.", Now).IsSuccess);
        Assert.Equal("Stable income, low LTV.", Assert.Single(application.Decisions).OverrideReason);
    }

    [Fact]
    public void Rejection_needs_a_reason()
    {
        var application = _f.In(ApplicationStatus.Underwriting);

        Assert.Equal(ApplicationErrors.ReasonRequired, application.Reject(_f.Underwriter, UnderwriterAuthority, "", Now).Error);
        Assert.True(application.Reject(_f.Underwriter, UnderwriterAuthority, "Unstable income.", Now).IsSuccess);
        Assert.Equal("Unstable income.", application.History[^1].Reason);
    }

    [Fact]
    public void Escalation_after_two_working_days_requires_a_senior_underwriter_and_is_idempotent()
    {
        var application = _f.In(ApplicationStatus.Underwriting);
        var calendar = new WorkingDayCalendar([]);

        // Entered on Monday 10:00 → deadline Wednesday 10:00.
        Assert.Equal(Now.AddDays(2), application.EscalationDeadline(calendar, 2));
        Assert.True(application.Escalate(Now.AddDays(2).AddMinutes(1)).IsSuccess);
        Assert.True(application.Escalate(Now.AddDays(3)).IsSuccess);

        Assert.Equal(Now.AddDays(2).AddMinutes(1), application.EscalatedAt);
        Assert.Single(application.DomainEvents.OfType<ApplicationEscalated>());
        Assert.Null(application.EscalationDeadline(calendar, 2));
        Assert.Equal(ApplicationErrors.AuthorityTooLow, application.Approve(_f.Underwriter, UnderwriterAuthority, [], null, Now).Error);
        Assert.True(application.Approve(_f.Senior, SeniorAuthority, [], null, Now).IsSuccess);
    }

    // ----- Counter-offer -----

    [Fact]
    public void An_accepted_counter_offer_replaces_the_terms_and_scoring_runs_again()
    {
        var application = _f.In(ApplicationStatus.Underwriting);

        Assert.True(application.CounterOffer(_f.Underwriter, UnderwriterAuthority, Terms(8_000m, 36), "Lower amount.", Now).IsSuccess);
        Assert.Equal(ApplicationStatus.CounterOffered, application.Status);
        Assert.True(application.AcceptCounterOffer(_f.Operator, Now.AddDays(1)).IsSuccess);

        Assert.Equal(ApplicationStatus.Scoring, application.Status);
        Assert.Equal(8_000m, application.Terms.Amount.Amount);
        Assert.Equal(36, application.Terms.TermMonths);
        Assert.Null(application.ProposedTerms);
    }

    [Fact]
    public void After_a_counter_offer_another_underwriter_decides()
    {
        var application = _f.In(ApplicationStatus.Underwriting);
        application.CounterOffer(_f.Underwriter, UnderwriterAuthority, Terms(8_000m), "Lower amount.", Now);
        application.AcceptCounterOffer(_f.Operator, Now);
        application.CompleteScoring(Passing(), ExchangeRate.Gel(Today), ApprovalTier.Underwriter, Now);

        Assert.Equal(ApplicationErrors.AlreadyDecided, application.Approve(_f.Underwriter, UnderwriterAuthority, [], null, Now).Error);
        Assert.True(application.Approve(_f.Underwriter2, UnderwriterAuthority, [], null, Now).IsSuccess);
    }

    [Fact]
    public void Counter_offer_terms_must_fit_the_snapshot()
    {
        var application = _f.In(ApplicationStatus.Underwriting);

        var result = application.CounterOffer(_f.Underwriter, UnderwriterAuthority, Terms(8_000m, rate: 30m), null, Now);

        Assert.Equal(ApplicationErrors.RateOutOfRange, result.Error);
    }

    [Fact]
    public void A_declined_counter_offer_cancels_and_an_unanswered_one_expires()
    {
        var declined = _f.In(ApplicationStatus.Underwriting);
        declined.CounterOffer(_f.Underwriter, UnderwriterAuthority, Terms(8_000m), null, Now);
        Assert.True(declined.DeclineCounterOffer(_f.Operator, "Client needs the full amount.", Now).IsSuccess);
        Assert.Equal(ApplicationStatus.Cancelled, declined.Status);

        var unanswered = _f.In(ApplicationStatus.Underwriting);
        unanswered.CounterOffer(_f.Underwriter, UnderwriterAuthority, Terms(8_000m), null, Now);
        Assert.Equal(ApplicationErrors.OfferExpired, unanswered.AcceptCounterOffer(_f.Operator, Now.AddDays(30)).Error);
        Assert.Equal("Application.WrongStatus", unanswered.Expire(Now.AddDays(29)).Error?.Code);
        Assert.True(unanswered.Expire(Now.AddDays(30)).IsSuccess);
        Assert.Equal(ApplicationStatus.Expired, unanswered.Status);
    }

    // ----- Credit committee -----

    [Fact]
    public void Two_matching_votes_of_three_decide()
    {
        var application = _f.In(ApplicationStatus.CreditCommittee, ApprovalTier.Committee);

        Assert.True(application.Vote(_f.Member1, approve: true, null, 2, Now).IsSuccess);
        Assert.True(application.Vote(_f.Member2, approve: false, "Too much exposure.", 2, Now).IsSuccess);
        Assert.Equal(ApplicationStatus.CreditCommittee, application.Status);
        Assert.True(application.Vote(_f.Member3, approve: true, null, 2, Now).IsSuccess);

        Assert.Equal(ApplicationStatus.Approved, application.Status);
        Assert.Equal(3, application.Decisions.Count);
    }

    [Fact]
    public void Two_reject_votes_reject_and_every_member_votes_once_with_a_reason_for_rejection()
    {
        var application = _f.In(ApplicationStatus.CreditCommittee, ApprovalTier.Committee);

        Assert.Equal(ApplicationErrors.ReasonRequired, application.Vote(_f.Member1, approve: false, null, 2, Now).Error);
        application.Vote(_f.Member1, approve: false, "High PTI.", 2, Now);
        Assert.Equal(ApplicationErrors.AlreadyDecided, application.Vote(_f.Member1, approve: false, "Again.", 2, Now).Error);
        Assert.Equal(ApplicationErrors.CreatorCannotDecide, application.Vote(_f.Operator, approve: true, null, 2, Now).Error);
        application.Vote(_f.Member2, approve: false, "Bureau history.", 2, Now);

        Assert.Equal(ApplicationStatus.Rejected, application.Status);
    }

    [Fact]
    public void Underwriters_do_not_decide_committee_files()
    {
        var application = _f.In(ApplicationStatus.CreditCommittee, ApprovalTier.Committee);

        Assert.Equal("Application.WrongStatus", application.Approve(_f.Senior, SeniorAuthority, [], null, Now).Error?.Code);
    }

    // ----- Contract, conditions, disbursement -----

    [Fact]
    public void Contract_is_signed_within_the_offer_and_conditions_follow()
    {
        var application = _f.In(ApplicationStatus.Approved);

        Assert.Equal(ApplicationErrors.OfferExpired, application.SignContract(_f.Operator, "LN-2026-000001", Now.AddDays(30)).Error);
        Assert.True(application.SignContract(_f.Operator, "LN-2026-000001", Now.AddDays(29)).IsSuccess);
        Assert.Equal(ApplicationStatus.ContractSigned, application.Status);
        Assert.Equal("LN-2026-000001", application.ContractNumber);

        Assert.True(application.MarkConditionMet(_f.Operator, application.Conditions[0].Id, Now.AddDays(29)).IsSuccess);
        Assert.Equal(ApplicationStatus.ConditionsMet, application.Status);
    }

    [Fact]
    public void Without_conditions_signing_also_meets_the_conditions()
    {
        var plain = new ApplicationFixture(ConsumerTerms() with { StandardConditions = [] });
        var application = plain.In(ApplicationStatus.Approved);

        application.SignContract(plain.Operator, "LN-2026-000002", Now);

        Assert.Equal(ApplicationStatus.ConditionsMet, application.Status);
        Assert.Equal(
            [ApplicationStatus.ContractSigned, ApplicationStatus.ConditionsMet],
            application.History.TakeLast(2).Select(h => h.To));
    }

    [Fact]
    public void Disbursement_needs_valid_identity_documents_on_the_day()
    {
        var application = _f.In(ApplicationStatus.ConditionsMet);

        var result = application.RequestDisbursement(_f.Clients, new DateOnly(2030, 1, 2));

        Assert.Equal("Application.IdentityDocumentInvalid", result.Error?.Code);
        Assert.False(application.DisbursementRequested);
    }

    [Fact]
    public void Once_disbursement_is_requested_the_application_cannot_be_cancelled_until_it_fails()
    {
        var application = _f.In(ApplicationStatus.ConditionsMet);
        Assert.True(application.RequestDisbursement(_f.Clients, Today).IsSuccess);

        Assert.Equal(ApplicationErrors.DisbursementInProgress, application.Cancel(_f.Operator, "Client changed mind.", Now).Error);
        Assert.True(application.DisbursementFailed().IsSuccess);
        Assert.True(application.Cancel(_f.Operator, "Client changed mind.", Now).IsSuccess);
    }

    [Fact]
    public void Disbursed_then_active_then_closed()
    {
        var application = _f.In(ApplicationStatus.Closed);

        Assert.False(application.IsOpen);
        Assert.NotNull(application.DisbursedAt);
        Assert.Equal(
            [
                ApplicationStatus.Draft, ApplicationStatus.Submitted, ApplicationStatus.PreCheck,
                ApplicationStatus.DocumentsPending, ApplicationStatus.Scoring, ApplicationStatus.Underwriting,
                ApplicationStatus.Approved, ApplicationStatus.ContractSigned, ApplicationStatus.ConditionsMet,
                ApplicationStatus.Disbursed, ApplicationStatus.Active, ApplicationStatus.Closed,
            ],
            application.History.Select(h => h.To));
        Assert.Equal(application.History.Count, application.DomainEvents.OfType<ApplicationStatusChanged>().Count());
    }

    // ----- Cancellation -----

    public static TheoryData<ApplicationStatus> CancellableStatuses() => [.. ApplicationStateMachine.Cancellable];

    [Theory]
    [MemberData(nameof(CancellableStatuses))]
    public void Cancellation_is_possible_before_disbursement_with_a_reason(ApplicationStatus status)
    {
        var application = Reach(status);

        Assert.Equal(ApplicationErrors.ReasonRequired, application.Cancel(_f.Operator, null, Now).Error);
        Assert.True(application.Cancel(_f.Operator, "Client withdrew.", Now).IsSuccess);
        Assert.Equal(ApplicationStatus.Cancelled, application.Status);
        Assert.False(application.IsOpen);
        Assert.Equal("Client withdrew.", application.History[^1].Reason);
    }

    [Theory]
    [InlineData(ApplicationStatus.Disbursed)]
    [InlineData(ApplicationStatus.Active)]
    [InlineData(ApplicationStatus.Closed)]
    public void Cancellation_is_impossible_after_disbursement(ApplicationStatus status)
    {
        var application = _f.In(status);
        application.DisbursementFailed();

        var result = application.Cancel(_f.Operator, "Too late.", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(status, application.Status);
    }

    [Fact]
    public void Actions_in_the_wrong_status_are_refused_and_change_nothing()
    {
        var application = _f.Draft();

        Assert.True(application.Approve(_f.Underwriter, UnderwriterAuthority, [], null, Now).IsFailure);
        Assert.True(application.StartPreCheck(Now).IsFailure);
        Assert.True(application.MarkDisbursed(Now).IsFailure);
        Assert.True(application.Activate(Now).IsFailure);
        Assert.True(application.SignContract(_f.Operator, "LN-2026-000003", Now).IsFailure);
        Assert.Equal(ApplicationStatus.Draft, application.Status);
        Assert.Single(application.History);
    }

    [Fact]
    public void Each_person_decides_once_across_compliance_and_underwriting()
    {
        var application = _f.In(ApplicationStatus.PreCheck);
        application.CompletePreCheck(PreCheckOutcome.ScreeningMatch, null, Now);
        application.DecideCompliance(_f.Senior, true, "Cleared.", Now);
        _f.UploadChecklist(application, DocumentType.IdentityDocumentCopy, DocumentType.IncomeStatement);
        application.SendToScoring(_f.Operator, Now);
        application.CompleteScoring(Passing(), ExchangeRate.Gel(Today), ApprovalTier.Underwriter, Now);

        Assert.Equal(ApplicationErrors.AlreadyDecided, application.Approve(_f.Senior, SeniorAuthority, [], null, Now).Error);
    }

    /// <summary>Any status, including those off the main path.</summary>
    private LoanApplication Reach(ApplicationStatus status)
    {
        switch (status)
        {
            case ApplicationStatus.CreditCommittee:
                return _f.In(status, ApprovalTier.Committee);
            case ApplicationStatus.ComplianceReview:
                var screened = _f.In(ApplicationStatus.PreCheck);
                screened.CompletePreCheck(PreCheckOutcome.ScreeningMatch, null, Now);
                return screened;
            case ApplicationStatus.CounterOffered:
                var offered = _f.In(ApplicationStatus.Underwriting);
                offered.CounterOffer(_f.Underwriter, UnderwriterAuthority, Terms(8_000m), null, Now);
                return offered;
            case ApplicationStatus.ConditionallyApproved:
                var conditional = _f.In(ApplicationStatus.Underwriting);
                conditional.Approve(
                    _f.Underwriter, UnderwriterAuthority, [new RequestedCondition(ConditionType.Other, "Guarantee letter")], null, Now);
                return conditional;
            default:
                return _f.In(status);
        }
    }
}
