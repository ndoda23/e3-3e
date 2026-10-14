using System.Text;
using CreditFlow.Application.Applications;
using CreditFlow.Application.Disbursements;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Loans;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.ExternalServices;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Seeding;

/// <summary>
/// Demo applications for Development: at least one in every lasting status, in both branches, with the demo personas
/// as creators and deciders and the mock personal numbers wherever a scenario matters. Everything goes through the
/// domain (aggregates, the scoring engine with bureau facts, contract numbers, payouts, loans), dated back with the
/// domain's own time parameters, so histories, decisions and audit records are real. The first steps of a few
/// applications are left to the live pre-check (bureau down → stays in PreCheck; sanctions / PEP → ComplianceReview;
/// KYC failure → Rejected; clean → DocumentsPending). Runs once per database (<c>SeedHistory</c>) in one transaction.
/// Submitted, Scoring and Disbursed are passing states: the background pipeline moves them on within seconds, and
/// they appear in every history.
/// </summary>
internal sealed class DevLendingDataSeeder(
    CreditFlowDbContext db,
    ScoringEngine engine,
    IContractNumberGenerator contractNumbers,
    IDocumentStore documents,
    TimeProvider clock)
{
    public const string SeedId = "dev_0001_lending_demo_data";

    /// <summary>A minimal PDF, so seeded documents can be downloaded.</summary>
    private static readonly byte[] SamplePdf = Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj " +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    private readonly Dictionary<string, UserId> _users = [];
    private readonly Dictionary<string, LoanProduct> _products = [];
    private BranchId _vake;
    private BranchId _batumi;
    private ScoringRuleSet _ruleSet = null!;
    private ApprovalThresholds _thresholds = null!;
    private int _clients;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.SeedHistory.AnyAsync(s => s.SeedId == SeedId, cancellationToken))
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LoadAsync(cancellationToken);
        var now = clock.GetUtcNow();

        await SeedLiveScenariosAsync(now, cancellationToken);
        await SeedDecisionsAsync(now, cancellationToken);
        await SeedContractsAndPayoutsAsync(now, cancellationToken);
        await SeedLoansAsync(now, cancellationToken);
        await SeedEndingsAsync(now, cancellationToken);

        db.SeedHistory.Add(new SeedHistoryEntry { SeedId = SeedId, AppliedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // ----- Scenarios -----

    /// <summary>Submitted now; the live pre-check decides where they go.</summary>
    private async Task SeedLiveScenariosAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var draft = Draft(await ClientAsync("Ana", "Kapanadze", now), LendingSeedCatalog.Consumer, 4_000m, 18, 19m, "anna.gelashvili", _vake, now);
        AddDocuments(draft, [DocumentType.IdentityDocumentCopy], "anna.gelashvili", now);
        Save(draft);

        (string Number, string First, string Last, string Creator, BranchId Branch)[] live =
        [
            (MockPersonalNumbers.Clean, "Lasha", "Gelashvili", "salome.janelidze", _vake),
            (MockPersonalNumbers.BureauDown, "Mzia", "Tsereteli", "davit.maisuradze", _batumi),
            (MockPersonalNumbers.Sanctioned, "Sanctioned", "Test Person", "anna.gelashvili", _vake),
            (MockPersonalNumbers.Pep, "Exposed", "Test Person", "davit.maisuradze", _batumi),
            (MockPersonalNumbers.KycFailure, "Irma", "Kvaratskhelia", "salome.janelidze", _vake),
        ];
        foreach (var (number, first, last, creator, branch) in live)
        {
            var client = await ClientAsync(first, last, now, number, verified: false);
            if (await HasOpenConsumerApplicationAsync(client, cancellationToken))
            {
                continue;
            }

            var application = Draft(client, LendingSeedCatalog.Consumer, 6_000m, 24, 18m, creator, branch, now);
            Check(application.Submit(Context(application, [client], now), User(creator), now));
            Save(application);
        }
    }

    /// <summary>Files waiting for or carrying a decision.</summary>
    private async Task SeedDecisionsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Underwriting today (Underwriter tier) and one escalated after waiting more than two working days.
        Save(await ScoredAsync(await ClientAsync("Giorgi", "Lomidze", now), LendingSeedCatalog.Consumer, 8_000m, "anna.gelashvili", _vake, now, cancellationToken));
        var escalated = await ScoredAsync(await ClientAsync("Nana", "Japaridze", now), LendingSeedCatalog.Consumer, 15_000m, "davit.maisuradze", _batumi, now.AddDays(-6), cancellationToken);
        Check(escalated.Escalate(now.AddDays(-2)));
        Save(escalated);

        // Senior tier, and a counter-offer waiting for the client.
        Save(await ScoredAsync(await ClientAsync("Levan", "Gogua", now, income: 6_000m), LendingSeedCatalog.Consumer, 35_000m, "salome.janelidze", _vake, now, cancellationToken));
        var countered = await ScoredAsync(await ClientAsync("Tamar", "Beridze", now), LendingSeedCatalog.Consumer, 12_000m, "anna.gelashvili", _vake, now.AddDays(-1), cancellationToken);
        var lower = LoanTerms.Create(Money.Of(9_000m, Currency.GEL), 24, 20m, ScheduleType.Annuity).Value;
        Check(countered.CounterOffer(User("nika.beridze"), Underwriter, lower, "Income supports 9,000 GEL.", now.AddHours(-20)));
        Save(countered);

        // Credit committee: a mortgage above the senior limit with one vote of two.
        var committee = await ScoredAsync(await ClientAsync("David", "Abashidze", now, income: 7_000m), LendingSeedCatalog.Mortgage, 150_000m, "anna.gelashvili", _vake, now.AddDays(-1), cancellationToken);
        Check(committee.Vote(User("irakli.tsereteli"), true, null, _thresholds.RequiredVotes, now.AddHours(-3)));
        Save(committee);

        // Approved (offer valid) and conditionally approved.
        var approved = await ScoredAsync(await ClientAsync("Eka", "Chikovani", now), LendingSeedCatalog.Consumer, 5_000m, "davit.maisuradze", _batumi, now.AddDays(-2), cancellationToken);
        Check(approved.Approve(User("giorgi.kapanadze"), Underwriter, [], null, now.AddDays(-1)));
        Save(approved);
        var conditional = await ScoredAsync(await ClientAsync("Sandro", "Meladze", now), LendingSeedCatalog.Consumer, 18_000m, "salome.janelidze", _vake, now.AddDays(-2), cancellationToken);
        Check(conditional.Approve(User("nika.beridze"), Underwriter,
            [new RequestedCondition(ConditionType.LifeInsurance, "Life insurance policy assigned to the bank")], null, now.AddDays(-1)));
        Save(conditional);
    }

    /// <summary>Signed contracts, and payouts that are ready, pending (no answer yet) or rejected by core banking.</summary>
    private async Task SeedContractsAndPayoutsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Mortgage approved by the committee and signed: the two standard conditions are still open.
        var signed = await ScoredAsync(await ClientAsync("Keti", "Mchedlishvili", now, income: 8_000m), LendingSeedCatalog.Mortgage, 180_000m, "anna.gelashvili", _vake, now.AddDays(-5), cancellationToken);
        Check(signed.Vote(User("sophio.gogichaishvili"), true, null, _thresholds.RequiredVotes, now.AddDays(-4)));
        Check(signed.Vote(User("zurab.kapanadze"), true, null, _thresholds.RequiredVotes, now.AddDays(-4)));
        await SignAsync(signed, "anna.gelashvili", now.AddDays(-3), cancellationToken);
        Save(signed);

        var ready = await ApprovedAndSignedAsync(await ClientAsync("Mariam", "Kurdadze", now), 7_000m, "salome.janelidze", _vake, now.AddDays(-4), cancellationToken);
        Save(ready);

        // Requested, core banking has not answered: Pending until someone checks again (same idempotency key).
        var timedOut = await ClientAsync("Pending", "Payout Client", now, MockPersonalNumbers.CoreBankingTimeout);
        if (!await HasOpenConsumerApplicationAsync(timedOut, cancellationToken))
        {
            var pending = await ApprovedAndSignedAsync(timedOut, 9_000m, "davit.maisuradze", _batumi, now.AddDays(-4), cancellationToken);
            Check(pending.RequestDisbursement(Parties(pending, timedOut), BankTime.DateOf(now)));
            db.Disbursements.Add(Disbursement.Request(pending.Id, Payout(pending), User("davit.maisuradze"), now.AddMinutes(-30)));
            Save(pending);
        }

        // Rejected by core banking: no money moved, a retry uses the same key.
        var rejectedClient = await ClientAsync("Failed", "Payout Client", now, MockPersonalNumbers.CoreBankingError);
        if (!await HasOpenConsumerApplicationAsync(rejectedClient, cancellationToken))
        {
            var failed = await ApprovedAndSignedAsync(rejectedClient, 6_500m, "anna.gelashvili", _vake, now.AddDays(-4), cancellationToken);
            Check(failed.RequestDisbursement(Parties(failed, rejectedClient), BankTime.DateOf(now)));
            var payout = Disbursement.Request(failed.Id, Payout(failed), User("anna.gelashvili"), now.AddHours(-2));
            Check(payout.Fail("The beneficiary account is blocked (mock core banking).", now.AddHours(-2)));
            Check(failed.DisbursementFailed());
            db.Disbursements.Add(payout);
            Save(failed);
        }
    }

    /// <summary>Active loans: one paying on time, one with installments overdue, and one repaid early (closed).</summary>
    private async Task SeedLoansAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (_, onTime) = await DisbursedLoanAsync(await ClientAsync("Nino", "Gvaramia", now), 12_000m, "anna.gelashvili", _vake, now.AddMonths(-4).AddDays(-3), cancellationToken);
        PayDue(onTime, now);

        var (_, late) = await DisbursedLoanAsync(await ClientAsync("Zaza", "Kiknadze", now), 9_500m, "davit.maisuradze", _batumi, now.AddMonths(-3).AddDays(-5), cancellationToken);
        Check(late.PayInstallment(User("davit.maisuradze"), late.Installments[0].DueDate, BankTime.ToInstant(late.Installments[0].DueDate, new TimeOnly(11, 0))));

        var (application, repaid) = await DisbursedLoanAsync(await ClientAsync("Ketevan", "Dolidze", now), 4_000m, "salome.janelidze", _vake, now.AddMonths(-2), cancellationToken);
        var payDay = repaid.DisbursedOn.AddDays(20);
        var repaidAt = BankTime.ToInstant(payDay, new TimeOnly(12, 0));
        Check(repaid.RepayEarly(null, User("salome.janelidze"), payDay, repaidAt));
        Check(application.Close(repaidAt));
    }

    /// <summary>The ways an application ends without a loan.</summary>
    private async Task SeedEndingsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A hard scoring rule fails: 45 days late at another bank three months ago.
        var latePayer = await ClientAsync("Late", "Payer", now, MockPersonalNumbers.LatePayer);
        if (!await HasOpenConsumerApplicationAsync(latePayer, cancellationToken))
        {
            var bureau = new BureauReport([new Delinquency(BankTime.DateOf(now).AddMonths(-3), 45)], []);
            Save(await ScoredAsync(latePayer, LendingSeedCatalog.Consumer, 7_000m, "salome.janelidze", _vake, now.AddDays(-3), cancellationToken, bureau));
        }

        var rejected = await ScoredAsync(await ClientAsync("Paata", "Burduli", now), LendingSeedCatalog.Consumer, 11_000m, "davit.maisuradze", _batumi, now.AddDays(-6), cancellationToken);
        Check(rejected.Reject(User("nika.beridze"), Underwriter, "Employment started two months ago; income not yet stable.", now.AddDays(-5)));
        Save(rejected);

        var cancelled = Draft(await ClientAsync("Rusudan", "Kobakhidze", now), LendingSeedCatalog.Consumer, 3_000m, 12, 20m, "anna.gelashvili", _vake, now.AddDays(-8));
        Check(cancelled.Cancel(User("anna.gelashvili"), "The client withdrew the application.", now.AddDays(-7)));
        Save(cancelled);

        var expired = await ScoredAsync(await ClientAsync("Shota", "Arveladze", now), LendingSeedCatalog.Consumer, 6_000m, "salome.janelidze", _vake, now.AddDays(-45), cancellationToken);
        Check(expired.Approve(User("giorgi.kapanadze"), Underwriter, [], null, now.AddDays(-44)));
        Check(expired.Expire(now.AddDays(-13)));
        Save(expired);
    }

    // ----- Building blocks -----

    private static readonly DeciderAuthority Underwriter = new(ApprovalTier.Underwriter, CanOverride: false);
    private static readonly DeciderAuthority Senior = new(ApprovalTier.SeniorUnderwriter, CanOverride: true);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        foreach (var user in await db.UserDirectory.AsNoTracking().Select(u => new { u.UserName, u.Id }).ToListAsync(cancellationToken))
        {
            _users[user.UserName] = new UserId(user.Id);
        }

        foreach (var product in await db.LoanProducts.Include(p => p.Versions).ToListAsync(cancellationToken))
        {
            _products[product.Code] = product;
        }

        _vake = await db.Branches.Where(b => b.Code == BranchSeedCatalog.Vake).Select(b => b.Id).SingleAsync(cancellationToken);
        _batumi = await db.Branches.Where(b => b.Code == BranchSeedCatalog.Batumi).Select(b => b.Id).SingleAsync(cancellationToken);
        _ruleSet = await db.ScoringRuleSets.OrderByDescending(s => s.Version).FirstAsync(cancellationToken);
        _thresholds = await db.ApprovalThresholds.SingleAsync(cancellationToken);
    }

    private UserId User(string userName) =>
        _users.TryGetValue(userName, out var id) ? id : throw new InvalidOperationException($"The demo user {userName} is not seeded.");

    private async Task<Client> ClientAsync(
        string firstName, string lastName, DateTimeOffset now, string? personalNumber = null, decimal income = 3_500m, bool verified = true)
    {
        var today = BankTime.DateOf(now);
        var number = personalNumber ?? $"3500101{1000 + ++_clients}";
        var personal = PersonalNumber.Create(number).Value;
        var existing = await db.Clients.Include(c => c.Incomes).Include(c => c.Obligations).AsSplitQuery()
            .FirstOrDefaultAsync(c => c.PersonalNumber == personal);
        if (existing is not null)
        {
            return existing;
        }

        var client = Client.Register(
            PersonalNumber.Create(number).Value,
            new ClientDetails(firstName, lastName, new DateOnly(1985, 1, 1).AddDays(_clients * 97), "+995 599 " + number[^6..], null, "Tbilisi, Rustaveli Ave 12"),
            IdentityDocument.Create(IdentityDocumentType.IdCard, "12ID" + number[^5..], today.AddYears(-3), today.AddYears(7)).Value,
            today).Value;
        client.AddIncome(IncomeType.Salary, "Employer LLC", Money.Of(income, Currency.GEL));
        if (verified)
        {
            client.RecordChecks(KycStatus.Verified, ScreeningStatus.Clear, now.AddMonths(-6));
        }

        db.Clients.Add(client);
        return client;
    }

    /// <summary>
    /// Whether a mock-scenario client already has an open consumer application (a development database where the
    /// scenario was tried by hand before this seed ran); the scenario is then skipped instead of breaking the
    /// one-open-application rule.
    /// </summary>
    private async Task<bool> HasOpenConsumerApplicationAsync(Client client, CancellationToken cancellationToken)
    {
        var consumer = _products[LendingSeedCatalog.Consumer].Id;
        return db.Entry(client).State != EntityState.Added
               && await db.LoanApplications.AnyAsync(a => a.BorrowerId == client.Id && a.ProductId == consumer && a.IsOpen, cancellationToken);
    }

    private LoanApplication Draft(
        Client borrower, string productCode, decimal amount, int months, decimal rate, string creator, BranchId branch, DateTimeOffset at)
    {
        var terms = LoanTerms.Create(Money.Of(amount, Currency.GEL), months, rate, ScheduleType.Annuity).Value;
        var application = LoanApplication.Create(_products[productCode], borrower.Id, terms, branch, User(creator), at.AddHours(-2)).Value;
        if (productCode == LendingSeedCatalog.Mortgage)
        {
            Check(application.AddCollateral(
                CollateralType.RealEstate, "Apartment, 85 m², Tbilisi", Money.Of(amount * 1.6m, Currency.GEL),
                BankTime.DateOf(at).AddMonths(-1), BankTime.DateOf(at)));
        }

        return application;
    }

    /// <summary>
    /// Submitted, pre-checked, documents uploaded and scored by the engine with the client's facts at <paramref name="at"/>;
    /// routed by the thresholds (or rejected when a hard rule fails).
    /// </summary>
    private async Task<LoanApplication> ScoredAsync(
        Client borrower, string productCode, decimal amount, string creator, BranchId branch, DateTimeOffset at,
        CancellationToken cancellationToken, BureauReport? bureau = null)
    {
        var isMortgage = productCode == LendingSeedCatalog.Mortgage;
        var application = Draft(borrower, productCode, amount, isMortgage ? 180 : 36, isMortgage ? 11m : 18m, creator, branch, at);
        var by = User(creator);
        Check(application.Submit(Context(application, [borrower], at), by, at.AddHours(-1.5)));
        Check(application.StartPreCheck(at.AddHours(-1.4)));
        Check(application.CompletePreCheck(PreCheckOutcome.Clear, null, at.AddHours(-1.3)));
        AddDocuments(application, application.MissingDocuments(application.Snapshot!.Terms), creator, at.AddHours(-1));
        Check(application.SendToScoring(by, at.AddMinutes(-30)));

        var today = BankTime.DateOf(at);
        var rates = await RatesAsync(today, cancellationToken);
        var gel = rates[application.Terms.Amount.Currency].ToGel(application.Terms.Amount).Amount;
        var terms = application.Snapshot.Terms;
        var schedule = ScheduleCalculator.Build(
            application.Terms.Amount, application.Terms.AnnualRate, application.Terms.TermMonths, application.Terms.ScheduleType,
            today, application.Terms.Amount.Percent(terms.MonthlyFeePercent));
        var facts = new PartyFacts(
            borrower.Id, borrower.FullName, PartyRole.Borrower, borrower.Incomes.Select(i => i.MonthlyAmount).ToList(),
            borrower.Obligations.Select(o => o.MonthlyPayment).ToList(), 0m, bureau ?? new BureauReport([], []), 0m);
        var run = engine.Score(_ruleSet, new ScoringInput(
            application.Terms, terms, gel, schedule.LargestInstallment, application.Collaterals, [facts], rates, today), at.AddMinutes(-29));
        Check(application.CompleteScoring(run, rates[application.Terms.Amount.Currency], _thresholds.TierFor(gel), at.AddMinutes(-29)));
        return application;
    }

    private async Task<LoanApplication> ApprovedAndSignedAsync(
        Client borrower, decimal amount, string creator, BranchId branch, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var application = await ScoredAsync(borrower, LendingSeedCatalog.Consumer, amount, creator, branch, at, cancellationToken);
        Check(application.Approve(User("tamta.tatishvili"), Senior, [], null, at.AddHours(2)));
        await SignAsync(application, creator, at.AddDays(1), cancellationToken);
        return application;
    }

    /// <summary>Signed with the next contract number; conditions met when there are none (consumer loans).</summary>
    private async Task SignAsync(LoanApplication application, string creator, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var year = BankTime.DateOf(at).Year;
        var number = await contractNumbers.NextAsync(year, cancellationToken);
        Check(application.SignContract(User(creator), ContractService.Format(year, number), at));
    }

    /// <summary>Approved, signed, paid out (with a core banking transaction) and booked as an active loan.</summary>
    private async Task<(LoanApplication Application, Loan Loan)> DisbursedLoanAsync(
        Client borrower, decimal amount, string creator, BranchId branch, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var application = await ApprovedAndSignedAsync(borrower, amount, creator, branch, at.AddDays(-3), cancellationToken);
        var disbursedAt = at;
        Check(application.RequestDisbursement(Parties(application, borrower), BankTime.DateOf(disbursedAt)));
        var payout = Disbursement.Request(application.Id, Payout(application), User(creator), disbursedAt.AddMinutes(-1));
        Check(payout.Succeed($"CB{application.Id.Value:N}".ToUpperInvariant(), disbursedAt));
        Check(application.MarkDisbursed(disbursedAt));
        db.Disbursements.Add(payout);

        var loan = Loan.Book(application, disbursedAt.AddMinutes(1)).Value;
        Check(application.Activate(disbursedAt.AddMinutes(1)));
        db.Loans.Add(loan);
        Save(application);
        return (application, loan);
    }

    /// <summary>Every installment due before today, paid on its due date.</summary>
    private void PayDue(Loan loan, DateTimeOffset now)
    {
        var today = BankTime.DateOf(now);
        while (loan.NextInstallment is { } next && next.DueDate < today)
        {
            Check(loan.PayInstallment(User("anna.gelashvili"), next.DueDate, BankTime.ToInstant(next.DueDate, new TimeOnly(11, 0))));
        }
    }

    private void AddDocuments(LoanApplication application, IEnumerable<DocumentType> types, string uploader, DateTimeOffset at)
    {
        foreach (var type in types)
        {
            var id = ApplicationLineId.New();
            Check(application.AddDocument(
                id, type, $"{type}.pdf", "application/pdf", SamplePdf.Length,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(SamplePdf)), User(uploader), at));
            documents.Add(id, SamplePdf);
        }
    }

    private SubmissionContext Context(LoanApplication application, IReadOnlyList<Client> clients, DateTimeOffset at) =>
        new(_products.Values.Single(p => p.Id == application.ProductId), ExchangeRate.Gel(BankTime.DateOf(at)),
            clients.ToDictionary(c => c.Id), false, BankTime.DateOf(at));

    private static Dictionary<ClientId, Client> Parties(LoanApplication application, Client borrower) =>
        application.Parties.Count == 1 ? new Dictionary<ClientId, Client> { [borrower.Id] = borrower }
            : throw new InvalidOperationException("Demo payouts have one party.");

    private static Money Payout(LoanApplication application) =>
        application.Terms.Amount - application.Terms.Amount.Percent(application.Snapshot!.Terms.UpfrontFeePercent);

    private async Task<Dictionary<Currency, ExchangeRate>> RatesAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var rates = new Dictionary<Currency, ExchangeRate> { [Currency.GEL] = ExchangeRate.Gel(day) };
        foreach (var currency in new[] { Currency.USD, Currency.EUR })
        {
            var rate = await db.FxRates.Where(r => r.Currency == currency && r.EffectiveDate <= day)
                .OrderByDescending(r => r.EffectiveDate).FirstAsync(cancellationToken);
            rates[currency] = rate.ToExchangeRate();
        }

        return rates;
    }

    private void Save(LoanApplication application)
    {
        if (db.Entry(application).State == EntityState.Detached)
        {
            db.LoanApplications.Add(application);
        }
    }

    private static void Check(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Demo data could not be created: {result.Error!.Description}");
        }
    }

    private static void Check<T>(Result<T> result) => Check(result.IsSuccess ? Result.Success() : Result.Failure(result.Error!));
}
