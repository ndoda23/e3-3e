using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Builds clients and applications straight through the domain (so a test controls who created and decided them and
/// which parties they have) and saves them; the pre-check and scoring are skipped with a passing scoring run.
/// </summary>
public sealed class TestLending(IServiceProvider services, TimeProvider clock)
{
    public static readonly DateOnly BirthDate = new(1990, 1, 1);

    private int _counter;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A new personal number that triggers no mock scenario.</summary>
    public string NextPersonalNumber() => $"0200101{1000 + Interlocked.Increment(ref _counter)}";

    public Task<ClientId> ClientAsync(string? personalNumber = null) =>
        InScopeAsync(async sp =>
        {
            var number = personalNumber ?? NextPersonalNumber();
            var client = Client.Register(
                PersonalNumber.Create(number).Value,
                new ClientDetails("Test", "Client", BirthDate, "+995 555 000000", null, "Tbilisi"),
                IdentityDocument.Create(IdentityDocumentType.IdCard, "ID" + number, new DateOnly(2020, 1, 1), new DateOnly(2035, 1, 1)).Value,
                BankTime.DateOf(clock.GetUtcNow())).Value;
            var db = sp.GetRequiredService<CreditFlowDbContext>();
            db.Clients.Add(client);
            await db.SaveChangesAsync(Ct);
            return client.Id;
        });

    /// <summary>
    /// An application already scored and waiting for a decision (Underwriting or Credit Committee by the seeded
    /// thresholds) in Vake. A mortgage gets a property worth twice the amount.
    /// </summary>
    public Task<LoanApplicationId> InReviewAsync(
        string productCode, decimal amount, UserId createdBy, ClientId? borrower = null, params ClientId[] guarantors) =>
        SaveAsync(productCode, amount, createdBy, borrower, guarantors, decide: null);

    /// <summary>The same, decided: approved by <paramref name="deciders"/> (one underwriter, or two committee votes).</summary>
    public Task<LoanApplicationId> ApprovedAsync(
        string productCode, decimal amount, UserId createdBy, IReadOnlyList<UserId> deciders, ClientId? borrower = null,
        params ClientId[] guarantors) =>
        SaveAsync(productCode, amount, createdBy, borrower, guarantors, (application, now) =>
        {
            if (application.Status == ApplicationStatus.CreditCommittee)
            {
                Check(application.Vote(deciders[0], true, null, 2, now));
                Check(application.Vote(deciders[1], true, null, 2, now));
            }
            else
            {
                Check(application.Approve(deciders[0], new DeciderAuthority(ApprovalTier.SeniorUnderwriter, true), [], null, now));
            }
        });

    private Task<LoanApplicationId> SaveAsync(
        string productCode, decimal amount, UserId createdBy, ClientId? borrowerId, ClientId[] guarantors,
        Action<LoanApplication, DateTimeOffset>? decide)
    {
        return InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CreditFlowDbContext>();
            var now = clock.GetUtcNow();
            var today = BankTime.DateOf(now);
            var borrower = borrowerId ?? await ClientAsync();

            var product = await db.LoanProducts.Include(p => p.Versions).SingleAsync(p => p.Code == productCode, Ct);
            var branch = await db.Branches.Where(b => b.Code == BranchSeedCatalog.Vake).Select(b => b.Id).SingleAsync(Ct);
            var isMortgage = productCode == LendingSeedCatalog.Mortgage;
            var terms = LoanTerms.Create(Money.Of(amount, Currency.GEL), isMortgage ? 120 : 24, isMortgage ? 10m : 18m, ScheduleType.Annuity).Value;
            var application = LoanApplication.Create(product, borrower, terms, branch, createdBy, now).Value;
            foreach (var guarantor in guarantors)
            {
                Check(application.AddParty(guarantor, PartyRole.Guarantor));
            }

            if (isMortgage)
            {
                Check(application.AddCollateral(
                    CollateralType.RealEstate, "Flat in Vake", Money.Of(amount * 2, Currency.GEL), today.AddMonths(-1), today));
            }

            var partyIds = application.Parties.Select(p => p.ClientId).ToList();
            var clients = await db.Clients.Where(c => partyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, Ct);
            Check(application.Submit(new SubmissionContext(product, ExchangeRate.Gel(today), clients, false, today), createdBy, now));
            Check(application.StartPreCheck(now));
            Check(application.CompletePreCheck(PreCheckOutcome.Clear, null, now));
            foreach (var type in application.MissingDocuments(application.Snapshot!.Terms))
            {
                Check(application.AddDocument(
                    ApplicationLineId.New(), type, $"{type}.pdf", "application/pdf", 1_000, new string('0', 64), createdBy, now));
            }

            Check(application.SendToScoring(createdBy, now));
            var run = new ScoringRun(1, now, [new RuleResult("PTI", true, true, "20.00%", "45%", "PTI 20.00% <= 45%.")]);
            Check(application.CompleteScoring(run, ExchangeRate.Gel(today), ApprovalThresholds.Default().TierFor(amount), now));
            decide?.Invoke(application, now);

            db.LoanApplications.Add(application);
            await db.SaveChangesAsync(Ct);
            return application.Id;
        });
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private static void Check(Result result) => Assert.True(result.IsSuccess, result.Error?.Description);

    private static void Check<T>(Result<T> result) => Assert.True(result.IsSuccess, result.Error?.Description);
}
