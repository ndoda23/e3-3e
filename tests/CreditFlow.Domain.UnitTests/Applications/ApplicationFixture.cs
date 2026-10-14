using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.UnitTests.Applications;

/// <summary>Builds applications in any status by driving them through the real aggregate methods.</summary>
internal sealed class ApplicationFixture
{
    public static readonly DateOnly Today = new(2026, 10, 12);
    public static readonly DateTimeOffset Now = BankTime.ToInstant(Today, new TimeOnly(10, 0));

    public UserId Operator { get; } = UserId.New();
    public UserId Compliance { get; } = UserId.New();
    public UserId Underwriter { get; } = UserId.New();
    public UserId Underwriter2 { get; } = UserId.New();
    public UserId Senior { get; } = UserId.New();
    public UserId Member1 { get; } = UserId.New();
    public UserId Member2 { get; } = UserId.New();
    public UserId Member3 { get; } = UserId.New();

    public static readonly DeciderAuthority UnderwriterAuthority = new(ApprovalTier.Underwriter, CanOverride: false);
    public static readonly DeciderAuthority SeniorAuthority = new(ApprovalTier.SeniorUnderwriter, CanOverride: true);

    public BranchId Branch { get; } = BranchId.New();

    public LoanProduct Product { get; }

    public Client Borrower { get; }

    public Client CoBorrower { get; }

    public ApplicationFixture(ProductTerms? terms = null, ProductType type = ProductType.Consumer)
    {
        Product = LoanProduct.Create("CONS", "Consumer loan", type, terms ?? ConsumerTerms(), Now).Value;
        Borrower = NewClient("01001011001", new DateOnly(1990, 5, 17));
        CoBorrower = NewClient("01001011002", new DateOnly(1992, 1, 1));
    }

    public static ProductTerms ConsumerTerms() => new()
    {
        MinAmountGel = 500m,
        MaxAmountGel = 200_000m,
        MinTermMonths = 6,
        MaxTermMonths = 60,
        MinRate = 10m,
        MaxRate = 24m,
        Currencies = [Currency.GEL, Currency.USD],
        ScheduleTypes = [ScheduleType.Annuity],
        RequiredDocuments =
        [
            new(DocumentType.IdentityDocumentCopy),
            new(DocumentType.IncomeStatement),
            new(DocumentType.EmploymentCertificate, MinAmountGel: 20_000m),
        ],
        StandardConditions = [ConditionType.LifeInsurance],
    };

    public static Client NewClient(string personalNumber, DateOnly birthDate, DateOnly? documentExpiry = null) =>
        Client.Register(
            PersonalNumber.Create(personalNumber).Value,
            new ClientDetails("Test", personalNumber, birthDate, "+995 555 000000", null, "Tbilisi"),
            IdentityDocument.Create(IdentityDocumentType.IdCard, "ID" + personalNumber, new DateOnly(2020, 1, 1),
                documentExpiry ?? new DateOnly(2030, 1, 1)).Value,
            Today).Value;

    public static LoanTerms Terms(decimal amount = 10_000m, int months = 24, decimal rate = 18m, Currency currency = Currency.GEL) =>
        LoanTerms.Create(Money.Of(amount, currency), months, rate, ScheduleType.Annuity).Value;

    public IReadOnlyDictionary<ClientId, Client> Clients => new Dictionary<ClientId, Client>
    {
        [Borrower.Id] = Borrower,
        [CoBorrower.Id] = CoBorrower,
    };

    public SubmissionContext Context(bool otherOpen = false, ExchangeRate? rate = null) =>
        new(Product, rate ?? ExchangeRate.Gel(Today), Clients, otherOpen, Today);

    public static ScoringRun Passing() => new(1, Now, [new RuleResult("PTI", true, true, "30.00 %", "≤ 45.00 %", "PTI within limit.")]);

    public static ScoringRun OverridableFailure() =>
        new(1, Now, [new RuleResult("PTI", false, true, "50.00 %", "≤ 45.00 %", "PTI above limit.")]);

    public static ScoringRun HardFailure() =>
        new(1, Now, [new RuleResult("BUREAU_DPD30_12M", false, false, "45 days", "< 30 days", "Overdue 30+ days.")]);

    public LoanApplication Draft(LoanTerms? terms = null) =>
        LoanApplication.Create(Product, Borrower.Id, terms ?? Terms(), Branch, Operator, Now).Value;

    /// <summary>An application driven to <paramref name="status"/> along the main path.</summary>
    public LoanApplication In(ApplicationStatus status, ApprovalTier tier = ApprovalTier.Underwriter)
    {
        var application = Draft(tier == ApprovalTier.Committee ? Terms(150_000m) : null);
        var steps = new List<(ApplicationStatus Reached, Func<Result> Step)>
        {
            (ApplicationStatus.Submitted, () => application.Submit(Context(), Operator, Now)),
            (ApplicationStatus.PreCheck, () => application.StartPreCheck(Now)),
            (ApplicationStatus.DocumentsPending, () => application.CompletePreCheck(PreCheckOutcome.Clear, null, Now)),
            (ApplicationStatus.Scoring, () => UploadChecklistAndScore(application)),
            (tier == ApprovalTier.Committee ? ApplicationStatus.CreditCommittee : ApplicationStatus.Underwriting,
                () => application.CompleteScoring(Passing(), ExchangeRate.Gel(Today), tier, Now)),
            (ApplicationStatus.Approved, () => tier == ApprovalTier.Committee
                ? Votes(application)
                : application.Approve(Underwriter, UnderwriterAuthority, [], null, Now)),
            (ApplicationStatus.ContractSigned, () => application.SignContract(Operator, "LN-2026-000001", Now.AddDays(1))),
            (ApplicationStatus.ConditionsMet, () => application.MarkConditionMet(Operator, application.Conditions[0].Id, Now.AddDays(2))),
            (ApplicationStatus.Disbursed, () => RequestAndDisburse(application)),
            (ApplicationStatus.Active, () => application.Activate(Now.AddDays(3))),
            (ApplicationStatus.Closed, () => application.Close(Now.AddDays(400))),
        };

        foreach (var (reached, step) in steps)
        {
            if (application.Status == status)
            {
                break;
            }

            var result = step();
            Assert.True(result.IsSuccess, $"Step to {reached} failed: {result.Error?.Description}");
        }

        Assert.Equal(status, application.Status);
        return application;
    }

    public void UploadChecklist(LoanApplication application, params DocumentType[] types)
    {
        foreach (var type in types)
        {
            Assert.True(application.AddDocument(
                ApplicationLineId.New(), type, $"{type}.pdf", "application/pdf", 1000, new string('a', 64), Operator, Now).IsSuccess);
        }
    }

    private Result UploadChecklistAndScore(LoanApplication application)
    {
        UploadChecklist(application, application.MissingDocuments(application.Snapshot!.Terms).ToArray());
        return application.SendToScoring(Operator, Now);
    }

    private Result Votes(LoanApplication application)
    {
        var first = application.Vote(Member1, approve: true, null, 2, Now);
        return first.IsFailure ? first : application.Vote(Member2, approve: true, null, 2, Now);
    }

    private Result RequestAndDisburse(LoanApplication application)
    {
        var requested = application.RequestDisbursement(Clients, Today.AddDays(3));
        return requested.IsFailure ? requested : application.MarkDisbursed(Now.AddDays(3));
    }
}
