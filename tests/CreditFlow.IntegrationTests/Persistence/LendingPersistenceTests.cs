using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Calendar;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Persistence;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.IntegrationTests.Persistence;

/// <summary>Every lending repository against SQL Server: round trips, ordering, concurrency and database rules.</summary>
public sealed class LendingPersistenceTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    private static readonly DateOnly Today = new(2026, 10, 12);
    private static readonly DateTimeOffset Now = BankTime.ToInstant(Today, new TimeOnly(10, 0));

    // ----- Reference data -----

    [Fact]
    public async Task Lending_reference_data_is_seeded()
    {
        var (products, ruleSet, thresholds, usd, holidays) = await Host.InScopeAsync(async sp => (
            await sp.GetRequiredService<CreditFlowDbContext>().LoanProducts.Include(p => p.Versions).ToListAsync(Ct),
            await sp.GetRequiredService<IScoringRuleSetRepository>().GetLatestAsync(Ct),
            await sp.GetRequiredService<IApprovalThresholdsRepository>().GetAsync(Ct),
            await sp.GetRequiredService<IFxRateRepository>().FindEffectiveAsync(Currency.USD, Today, Ct),
            await sp.GetRequiredService<IHolidayRepository>().ListDatesAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Ct)));

        Assert.Equal(["AUTO", "CONS", "MORT"], products.Select(p => p.Code).Order());
        var consumer = products.Single(p => p.Code == LendingSeedCatalog.Consumer);
        Assert.Equal(2, consumer.CurrentVersion.Number);
        Assert.Equal(24m, consumer.CurrentVersion.Terms.MaxRate);
        Assert.Equal(26m, consumer.FindVersion(1)!.Terms.MaxRate);
        Assert.Equal(80m, products.Single(p => p.Code == LendingSeedCatalog.Mortgage).CurrentVersion.Terms.MaxLtvPercent);

        Assert.Equal(1, ruleSet!.Version);
        Assert.Equal(ScoringRuleSetSeed.Version1.Select(r => r.Code), ruleSet.Rules.Select(r => r.Code));
        Assert.Equal(20_000m, thresholds.UnderwriterMaxGel);
        Assert.Equal(2.7134m, usd!.GelPerUnit);
        Assert.Contains(new DateOnly(2026, 4, 12), holidays); // Orthodox Easter 2026
        Assert.Equal(17, holidays.Count);
    }

    [Fact]
    public async Task The_rate_in_effect_is_the_latest_on_or_before_the_date()
    {
        var (january, september, none) = await Host.InScopeAsync(async sp =>
        {
            var rates = sp.GetRequiredService<IFxRateRepository>();
            return (
                await rates.FindEffectiveAsync(Currency.EUR, new DateOnly(2026, 1, 1), Ct),
                await rates.FindEffectiveAsync(Currency.EUR, new DateOnly(2026, 9, 30), Ct),
                await rates.FindEffectiveAsync(Currency.EUR, new DateOnly(2025, 12, 31), Ct));
        });

        Assert.Equal(2.93m, january!.GelPerUnit);
        Assert.Equal(2.93m, september!.GelPerUnit);
        Assert.Null(none);
    }

    // ----- Clients and products -----

    [Fact]
    public async Task A_client_round_trips_with_document_incomes_and_obligations()
    {
        var client = NewClient("01001011001");
        client.AddIncome(IncomeType.Salary, "TBC Bank", Money.Of(3_000m, Currency.GEL));
        client.AddIncome(IncomeType.Rental, "Flat", Money.Of(400m, Currency.USD));
        client.AddObligation("Friend", Money.Of(100m, Currency.GEL), Money.Of(1_000m, Currency.GEL));
        await SaveAsync(db => db.Clients.Add(client));

        var loaded = await Host.InScopeAsync(sp => sp.GetRequiredService<IClientRepository>().GetAsync(client.Id, Ct));

        Assert.Equal("01001011001", loaded!.PersonalNumber.Value);
        Assert.Equal(client.IdentityDocument, loaded.IdentityDocument);
        Assert.Equal([Money.Of(3_000m, Currency.GEL), Money.Of(400m, Currency.USD)],
            loaded.Incomes.Select(i => i.MonthlyAmount).OrderBy(m => m.Currency));
        Assert.Equal(Money.Of(1_000m, Currency.GEL), Assert.Single(loaded.Obligations).OutstandingAmount);
        Assert.True(await Host.InScopeAsync(sp => sp.GetRequiredService<IClientRepository>()
            .PersonalNumberExistsAsync(client.PersonalNumber, Ct)));
    }

    [Fact]
    public async Task The_personal_number_is_unique_in_the_database()
    {
        await SaveAsync(db => db.Clients.Add(NewClient("01001011001")));

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(db => db.Clients.Add(NewClient("01001011001"))));
    }

    [Fact]
    public async Task A_new_product_version_is_saved_and_old_versions_stay()
    {
        var productId = await ProductIdAsync(LendingSeedCatalog.Mortgage);
        await Host.InScopeAsync(async sp =>
        {
            var product = (await sp.GetRequiredService<ILoanProductRepository>().GetAsync(productId, Ct))!;
            Assert.True(product.PublishVersion(product.CurrentVersion.Terms with { MaxRate = 13m }, Now).IsSuccess);
            await sp.GetRequiredService<CreditFlowDbContext>().SaveChangesAsync(Ct);
        });

        var reloaded = await Host.InScopeAsync(sp => sp.GetRequiredService<ILoanProductRepository>().GetAsync(productId, Ct));

        Assert.Equal([1, 2], reloaded!.Versions.Select(v => v.Number).Order());
        Assert.Equal(13m, reloaded.CurrentVersion.Terms.MaxRate);
        Assert.Equal(14m, reloaded.FindVersion(1)!.Terms.MaxRate);
    }

    // ----- Applications -----

    [Fact]
    public async Task An_application_round_trips_with_every_part()
    {
        var (applicationId, operatorId) = await CreateApprovedApplicationAsync();

        var loaded = await Host.InScopeAsync(sp => sp.GetRequiredService<ILoanApplicationRepository>().GetAsync(applicationId, Ct));

        Assert.Equal(ApplicationStatus.Approved, loaded!.Status);
        Assert.Equal(Money.Of(10_000m, Currency.GEL), loaded.Terms.Amount);
        Assert.Equal(24, loaded.Terms.TermMonths);
        Assert.Equal(LendingSeedCatalog.Consumer, loaded.Snapshot!.Code);
        Assert.Equal(2, loaded.Snapshot.Version);
        Assert.Equal(10_000m, loaded.AmountGel);
        Assert.Equal(ApprovalTier.Underwriter, loaded.Tier);
        Assert.Equal("PTI", Assert.Single(Assert.Single(loaded.ScoringRuns).Results).RuleCode);
        Assert.Equal(2, loaded.Documents.Count);
        Assert.Equal(DecisionKind.Approve, Assert.Single(loaded.Decisions).Kind);
        Assert.Equal(
            [
                ApplicationStatus.Draft, ApplicationStatus.Submitted, ApplicationStatus.PreCheck,
                ApplicationStatus.DocumentsPending, ApplicationStatus.Scoring, ApplicationStatus.Underwriting,
                ApplicationStatus.Approved,
            ],
            loaded.History.Select(h => h.To));
        Assert.Equal(operatorId, loaded.History[0].By);
        Assert.Equal(Now.AddDays(30), loaded.OfferExpiresAt);
    }

    [Fact]
    public async Task A_borrower_has_one_open_application_per_product_in_the_database()
    {
        var (applicationId, _) = await CreateApprovedApplicationAsync();
        var existing = await Host.QueryAsync(db => db.LoanApplications.SingleAsync(a => a.Id == applicationId, Ct));

        var hasOpen = await Host.InScopeAsync(sp => sp.GetRequiredService<ILoanApplicationRepository>()
            .HasOpenApplicationAsync(existing.BorrowerId, existing.ProductId, LoanApplicationId.New(), Ct));
        Assert.True(hasOpen);

        // A second open application for the same borrower and product is refused by the filtered unique index.
        await Assert.ThrowsAsync<DbUpdateException>(() => Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlowDbContext>();
            var product = await db.LoanProducts.Include(p => p.Versions).SingleAsync(p => p.Id == existing.ProductId, Ct);
            var second = LoanApplication.Create(product, existing.BorrowerId, Terms(), existing.BranchId, existing.CreatedBy, Now).Value;
            db.LoanApplications.Add(second);
            await db.SaveChangesAsync(Ct);
        }));
    }

    [Fact]
    public async Task Two_underwriters_cannot_both_decide_the_same_application()
    {
        var (applicationId, _) = await CreateApplicationInUnderwritingAsync();
        var (first, second) = (UserId.New(), UserId.New());

        // Both load version V; the first approval wins, the second fails on the rowversion.
        await using var scopeA = Host.Services.CreateAsyncScope();
        await using var scopeB = Host.Services.CreateAsyncScope();
        var a = (await scopeA.ServiceProvider.GetRequiredService<ILoanApplicationRepository>().GetAsync(applicationId, Ct))!;
        var b = (await scopeB.ServiceProvider.GetRequiredService<ILoanApplicationRepository>().GetAsync(applicationId, Ct))!;

        Assert.True(a.Approve(first, new DeciderAuthority(ApprovalTier.Underwriter, false), [], null, Now).IsSuccess);
        Assert.True(b.Reject(second, new DeciderAuthority(ApprovalTier.Underwriter, false), "No.", Now).IsSuccess);
        await scopeA.ServiceProvider.GetRequiredService<CreditFlowDbContext>().SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            scopeB.ServiceProvider.GetRequiredService<CreditFlowDbContext>().SaveChangesAsync(Ct));
        var stored = await Host.QueryAsync(db => db.LoanApplications.Include(x => x.Decisions).SingleAsync(x => x.Id == applicationId, Ct));
        Assert.Equal(ApplicationStatus.Approved, stored.Status);
        Assert.Single(stored.Decisions);
    }

    [Fact]
    public async Task Changing_only_child_rows_still_checks_the_root_rowversion()
    {
        // Two committee members vote at the same time on a file with no votes yet: each vote alone does not change the
        // status, but the second save must still fail so that it is retried with the first vote visible.
        var (applicationId, _) = await CreateApplicationInUnderwritingAsync(committee: true);
        var (member1, member2) = (UserId.New(), UserId.New());

        await using var scopeA = Host.Services.CreateAsyncScope();
        await using var scopeB = Host.Services.CreateAsyncScope();
        var a = (await scopeA.ServiceProvider.GetRequiredService<ILoanApplicationRepository>().GetAsync(applicationId, Ct))!;
        var b = (await scopeB.ServiceProvider.GetRequiredService<ILoanApplicationRepository>().GetAsync(applicationId, Ct))!;
        Assert.True(a.Vote(member1, approve: true, null, 2, Now).IsSuccess);
        Assert.True(b.Vote(member2, approve: true, null, 2, Now).IsSuccess);
        Assert.Equal(ApplicationStatus.CreditCommittee, a.Status);

        await scopeA.ServiceProvider.GetRequiredService<CreditFlowDbContext>().SaveChangesAsync(Ct);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            scopeB.ServiceProvider.GetRequiredService<CreditFlowDbContext>().SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task One_decision_per_person_is_also_a_database_rule()
    {
        var (applicationId, _) = await CreateApprovedApplicationAsync();

        var violation = await Assert.ThrowsAsync<SqlException>(() => Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlowDbContext>();
            var decidedBy = await db.LoanApplications.Where(a => a.Id == applicationId)
                .SelectMany(a => a.Decisions).Select(d => d.DecidedBy).SingleAsync(Ct);
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO ApplicationDecisions (Id, ApplicationId, Kind, DecidedBy, DecidedAt)
                VALUES ({Guid.NewGuid()}, {applicationId.Value}, 'Reject', {decidedBy.Value}, {Now})
                """,
                Ct);
        }));

        Assert.Equal(2601, violation.Number); // duplicate key in UX_ApplicationDecisions_OnePerPerson
    }

    // ----- Helpers -----

    private static LoanTerms Terms() => LoanTerms.Create(Money.Of(10_000m, Currency.GEL), 24, 18m, ScheduleType.Annuity).Value;

    private static Client NewClient(string personalNumber) =>
        Client.Register(
            PersonalNumber.Create(personalNumber).Value,
            new ClientDetails("Anna", "Gelashvili", new DateOnly(1990, 5, 17), "+995 555 000000", null, "Tbilisi"),
            IdentityDocument.Create(IdentityDocumentType.IdCard, "ID" + personalNumber, new DateOnly(2020, 1, 1), new DateOnly(2030, 1, 1)).Value,
            Today).Value;

    private Task SaveAsync(Action<CreditFlowDbContext> change) => Host.InScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<CreditFlowDbContext>();
        change(db);
        await db.SaveChangesAsync(Ct);
    });

    private Task<LoanProductId> ProductIdAsync(string code) =>
        Host.QueryAsync(db => db.LoanProducts.Where(p => p.Code == code).Select(p => p.Id).SingleAsync(Ct));

    private Task<UserId> OperatorAsync() =>
        Host.QueryAsync(async db => (await db.StaffMembers.OrderBy(s => s.Id).FirstAsync(Ct)).Id);

    private async Task<(LoanApplicationId Id, UserId Operator)> CreateApplicationInUnderwritingAsync(bool committee = false)
    {
        var operatorId = await OperatorAsync();
        var borrower = NewClient(committee ? "01001011009" : "01001011008");
        await SaveAsync(db => db.Clients.Add(borrower));

        var id = await Host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlowDbContext>();
            var product = await db.LoanProducts.Include(p => p.Versions).SingleAsync(p => p.Code == LendingSeedCatalog.Consumer, Ct);
            var branch = await db.Branches.OrderBy(b => b.Code).Select(b => b.Id).FirstAsync(Ct);
            var terms = committee ? LoanTerms.Create(Money.Of(45_000m, Currency.GEL), 24, 18m, ScheduleType.Annuity).Value : Terms();
            var application = LoanApplication.Create(product, borrower.Id, terms, branch, operatorId, Now).Value;
            var clients = new Dictionary<ClientId, Client> { [borrower.Id] = borrower };
            Assert.True(application.Submit(new SubmissionContext(product, ExchangeRate.Gel(Today), clients, false, Today), operatorId, Now).IsSuccess);
            application.StartPreCheck(Now);
            application.CompletePreCheck(PreCheckOutcome.Clear, null, Now);
            foreach (var type in application.MissingDocuments(application.Snapshot!.Terms))
            {
                application.AddDocument(ApplicationLineId.New(), type, $"{type}.pdf", "application/pdf", 10, new string('a', 64), operatorId, Now);
            }

            Assert.True(application.SendToScoring(operatorId, Now).IsSuccess);
            var run = new ScoringRun(1, Now, [new RuleResult("PTI", true, true, "30.00 %", "≤ 45.00 %", "ok")]);
            application.CompleteScoring(run, ExchangeRate.Gel(Today), committee ? ApprovalTier.Committee : ApprovalTier.Underwriter, Now);
            db.LoanApplications.Add(application);
            await db.SaveChangesAsync(Ct);
            return application.Id;
        });
        return (id, operatorId);
    }

    private async Task<(LoanApplicationId Id, UserId Operator)> CreateApprovedApplicationAsync()
    {
        var (id, operatorId) = await CreateApplicationInUnderwritingAsync();
        await Host.InScopeAsync(async sp =>
        {
            var application = (await sp.GetRequiredService<ILoanApplicationRepository>().GetAsync(id, Ct))!;
            Assert.True(application.Approve(UserId.New(), new DeciderAuthority(ApprovalTier.Underwriter, false), [], null, Now).IsSuccess);
            await sp.GetRequiredService<CreditFlowDbContext>().SaveChangesAsync(Ct);
        });
        return (id, operatorId);
    }
}
