using CreditFlow.Application.Common;
using CreditFlow.Application.Disbursements;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Loans;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.ExternalServices;
using CreditFlow.Infrastructure.Outbox;
using CreditFlow.Infrastructure.Persistence;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CreditFlow.IntegrationTests.Persistence;

/// <summary>
/// Contract signing and disbursement on SQL Server: gapless contract numbers (also under parallel signing and after a
/// rollback), idempotent payouts (twice in a row, twice in parallel, after a core banking error, after a timeout), the
/// exposure limit under parallel payouts sharing a guarantor, loan booking and repayments up to closing.
/// </summary>
public sealed class DisbursementWorkflowTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    private const string VakeOperator = "anna.gelashvili";
    private static readonly DateOnly Today = new(2026, 10, 12);

    private readonly FakeTimeProvider _clock = new(BankTime.ToInstant(Today, new TimeOnly(10, 0)));
    private TestLending? _lending;

    protected override string Environment => "Development";

    protected override bool SeedDevUsers => true;

    protected override TimeProvider? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["Mocks:CoreBankingTimeout"] = "00:00:00.200",
        ["Mocks:HangDuration"] = "00:00:05",
    };

    private TestLending Lending => _lending ??= new TestLending(Host.Services, _clock);

    private OutboxProcessor Processor => Host.Services.GetRequiredService<OutboxProcessor>();

    // ----- Contract numbers -----

    [Fact]
    public async Task Parallel_signings_get_gapless_unique_contract_numbers()
    {
        var applications = new List<LoanApplicationId>();
        for (var i = 0; i < 6; i++)
        {
            applications.Add(await ApprovedAsync(10_000m));
        }

        var operatorId = await UserAsync(VakeOperator);
        var results = await Task.WhenAll(applications.Select(id => Task.Run(
            () => AsAsync<ContractService, Result>(operatorId, s => s.SignAsync(id, Ct)), Ct)));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Description));
        var numbers = await Host.QueryAsync(db => db.LoanApplications
            .Where(a => a.ContractNumber != null).Select(a => a.ContractNumber!).ToListAsync(Ct));
        Assert.Equal(Enumerable.Range(1, 6).Select(n => ContractService.Format(2026, n)), numbers.Order(StringComparer.Ordinal));
        foreach (var id in applications)
        {
            Assert.Equal(ApplicationStatus.ConditionsMet, await StatusAsync(id));
        }
    }

    [Fact]
    public async Task A_failed_signing_gives_its_number_back()
    {
        var operatorId = await UserAsync(VakeOperator);
        var expired = await ApprovedAsync(10_000m);

        // A number taken in a transaction that rolls back is not used up.
        await Host.InScopeAsync(async sp =>
        {
            var result = await sp.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
                async token =>
                {
                    Assert.Equal(1, await sp.GetRequiredService<IContractNumberGenerator>().NextAsync(2026, token));
                    return Result.Failure(Error.Conflict("Test.Rollback", "Rolled back on purpose."));
                },
                Ct);
            Assert.True(result.IsFailure);
        });

        // The offer expired: the signing fails after taking a number, which is rolled back with it.
        _clock.Advance(TimeSpan.FromDays(31));
        var failed = await AsAsync<ContractService, Result>(operatorId, s => s.SignAsync(expired, Ct));
        Assert.Equal(ApplicationErrors.OfferExpired, failed.Error);

        var next = await ApprovedAsync(10_000m);
        AssertSuccess(await AsAsync<ContractService, Result>(operatorId, s => s.SignAsync(next, Ct)));
        Assert.Equal(ContractService.Format(2026, 1), await ContractNumberAsync(next));
    }

    // ----- Idempotent disbursement -----

    [Fact]
    public async Task A_payout_books_the_loan_and_a_second_request_pays_nothing_more()
    {
        var operatorId = await UserAsync(VakeOperator);
        var id = await ReadyToDisburseAsync(10_000m);

        var first = await DisburseAsync(operatorId, id);
        var second = await DisburseAsync(operatorId, id);

        Assert.Equal(DisbursementStatus.Succeeded, first.Value);
        Assert.Equal(DisbursementStatus.Succeeded, second.Value);
        await AssertPaidOnceAsync(id);

        await Processor.ProcessAllDueAsync(Ct);
        Assert.Equal(ApplicationStatus.Active, await StatusAsync(id));
        var loan = await Host.QueryAsync(db => db.Loans.Include(l => l.Installments).SingleAsync(l => l.ApplicationId == id, Ct));
        Assert.Equal(24, loan.Installments.Count);
        Assert.Equal(10_000m, loan.Installments.Sum(i => i.Principal.Amount));
        Assert.Equal(await ContractNumberAsync(id), loan.ContractNumber);

        // The payout is the principal less the 0.5 % upfront fee of the current consumer product version.
        var payout = await Host.QueryAsync(db => db.Disbursements.SingleAsync(d => d.ApplicationId == id, Ct));
        Assert.Equal(Money.Of(9_950m, Currency.GEL), payout.Amount);

        var audit = await Host.QueryAsync(db => db.AuditLogs.Where(a => a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync(Ct));
        Assert.Contains(AuditActions.DisbursementRequested, audit);
        Assert.Contains(AuditActions.DisbursementSucceeded, audit);
    }

    [Fact]
    public async Task Two_parallel_payout_requests_pay_once()
    {
        var operatorId = await UserAsync(VakeOperator);
        var id = await ReadyToDisburseAsync(10_000m);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => DisburseAsync(operatorId, id), Ct)));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Description));
        await AssertPaidOnceAsync(id);
        Assert.Equal(ApplicationStatus.Disbursed, await StatusAsync(id));
    }

    [Fact]
    public async Task A_rejected_payout_can_be_retried_with_the_same_key()
    {
        var operatorId = await UserAsync(VakeOperator);
        var id = await ReadyToDisburseAsync(10_000m, await Lending.ClientAsync(MockPersonalNumbers.CoreBankingError));

        var rejected = await DisburseAsync(operatorId, id);

        Assert.Equal(DisbursementErrors.Rejected, rejected.Error);
        Assert.Equal(ApplicationStatus.ConditionsMet, await StatusAsync(id));
        Assert.Equal(0, await Host.QueryAsync(db => db.Set<MockCoreBankingTransaction>().CountAsync(Ct)));
        var failed = await Host.QueryAsync(db => db.Disbursements.SingleAsync(d => d.ApplicationId == id, Ct));
        Assert.Equal(DisbursementStatus.Failed, failed.Status);

        var retried = await DisburseAsync(operatorId, id);

        Assert.Equal(DisbursementStatus.Succeeded, retried.Value);
        var payout = await AssertPaidOnceAsync(id);
        Assert.Equal(failed.IdempotencyKey, payout.IdempotencyKey);
        Assert.Equal(2, payout.Attempts);
    }

    [Fact]
    public async Task A_timed_out_payout_is_confirmed_in_the_background_with_the_original_transaction()
    {
        var operatorId = await UserAsync(VakeOperator);
        var id = await ReadyToDisburseAsync(10_000m, await Lending.ClientAsync(MockPersonalNumbers.CoreBankingTimeout));

        var pending = await DisburseAsync(operatorId, id);

        // Core banking made the transfer but answered too late: the payout stays Pending, the application cannot be cancelled.
        Assert.Equal(DisbursementStatus.Pending, pending.Value);
        var ledger = await Host.QueryAsync(db => db.Set<MockCoreBankingTransaction>().SingleAsync(Ct));
        Assert.Equal(ApplicationStatus.ConditionsMet, await StatusAsync(id));
        Assert.Equal(ApplicationErrors.DisbursementInProgress,
            (await AsAsync<CreditFlow.Application.Applications.ApplicationService, Result>(operatorId, s => s.CancelAsync(id, "Changed mind.", Ct))).Error);

        await Processor.ProcessAllDueAsync(Ct);

        var payout = await AssertPaidOnceAsync(id);
        Assert.Equal(ledger.TransactionId, payout.TransactionId);
        Assert.Equal(ApplicationStatus.Active, await StatusAsync(id));
    }

    // ----- Exposure limit -----

    [Fact]
    public async Task Of_two_parallel_payouts_that_together_exceed_a_guarantors_limit_exactly_one_succeeds()
    {
        // 300,000 GEL limit (seeded rule set): each 200,000 GEL mortgage fits alone, both together do not.
        var operatorId = await UserAsync(VakeOperator);
        var guarantor = await Lending.ClientAsync();
        var first = await ReadyToDisburseAsync(200_000m, null, guarantor);
        var second = await ReadyToDisburseAsync(200_000m, null, guarantor);

        var results = await Task.WhenAll(new[] { first, second }.Select(id => Task.Run(() => DisburseAsync(operatorId, id), Ct)));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Equal(DisbursementErrors.ExposureLimitExceeded, Assert.Single(results, r => r.IsFailure).Error);
        Assert.Equal(1, await Host.QueryAsync(db => db.Disbursements.CountAsync(Ct)));
        Assert.Equal(1, await Host.QueryAsync(db => db.Set<MockCoreBankingTransaction>().CountAsync(Ct)));
    }

    [Fact]
    public async Task A_repaid_loan_no_longer_counts_against_the_limit()
    {
        var operatorId = await UserAsync(VakeOperator);
        var guarantor = await Lending.ClientAsync();
        var first = await ReadyToDisburseAsync(200_000m, null, guarantor);
        var second = await ReadyToDisburseAsync(200_000m, null, guarantor);
        AssertSuccess(Untyped(await DisburseAsync(operatorId, first)));
        await Processor.ProcessAllDueAsync(Ct);

        Assert.Equal(DisbursementErrors.ExposureLimitExceeded, (await DisburseAsync(operatorId, second)).Error);

        var loanId = await Host.QueryAsync(db => db.Loans.Where(l => l.ApplicationId == first).Select(l => l.Id).SingleAsync(Ct));
        AssertSuccess(await AsAsync<LoanService, Result>(operatorId, s => s.RepayEarlyAsync(loanId, 150_000m, Ct)));

        Assert.Equal(DisbursementStatus.Succeeded, (await DisburseAsync(operatorId, second)).Value);
    }

    // ----- Repayments -----

    [Fact]
    public async Task Installments_and_a_full_early_repayment_close_the_loan_and_the_application()
    {
        var operatorId = await UserAsync(VakeOperator);
        var id = await ReadyToDisburseAsync(10_000m);
        AssertSuccess(Untyped(await DisburseAsync(operatorId, id)));
        await Processor.ProcessAllDueAsync(Ct);
        var loanId = await Host.QueryAsync(db => db.Loans.Where(l => l.ApplicationId == id).Select(l => l.Id).SingleAsync(Ct));

        AssertSuccess(await AsAsync<LoanService, Result>(operatorId, s => s.PayInstallmentAsync(loanId, Ct)));
        _clock.Advance(TimeSpan.FromDays(10));
        var quote = await AsAsync<LoanService, Result<EarlyRepaymentQuote>>(operatorId, s => s.QuoteEarlyRepaymentAsync(loanId, null, Ct));
        AssertSuccess(await AsAsync<LoanService, Result>(operatorId, s => s.RepayEarlyAsync(loanId, null, Ct)));
        await Processor.ProcessAllDueAsync(Ct);

        var loan = (await AsAsync<LoanService, LoanDetails?>(operatorId, s => s.GetAsync(loanId, Ct)))!;
        Assert.Equal(LoanStatus.Closed, loan.Status);
        Assert.True(loan.OutstandingPrincipal.IsZero);
        Assert.Equal([RepaymentKind.Installment, RepaymentKind.FullEarly], loan.Repayments.Select(r => r.Kind));
        Assert.Equal(quote.Value.Total, loan.Repayments[^1].Total);
        Assert.Equal(ApplicationStatus.Closed, await StatusAsync(id));
    }

    [Fact]
    public async Task A_loan_of_another_branch_is_not_found()
    {
        var operatorId = await UserAsync(VakeOperator);
        var id = await ReadyToDisburseAsync(10_000m);
        AssertSuccess(Untyped(await DisburseAsync(operatorId, id)));
        await Processor.ProcessAllDueAsync(Ct);
        var loanId = await Host.QueryAsync(db => db.Loans.Where(l => l.ApplicationId == id).Select(l => l.Id).SingleAsync(Ct));

        var batumi = await UserAsync("davit.maisuradze");

        Assert.Null(await AsAsync<LoanService, LoanDetails?>(batumi, s => s.GetAsync(loanId, Ct)));
        Assert.Equal(LoanErrors.NotFound, (await AsAsync<LoanService, Result>(batumi, s => s.PayInstallmentAsync(loanId, Ct))).Error);
        Assert.Equal(ApplicationErrors.NotFound, (await AsAsync<DisbursementService, Result<DisbursementStatus>>(batumi, s => s.DisburseAsync(id, Ct))).Error);
    }

    // ----- Helpers -----

    private static void AssertSuccess(Result result) => Assert.True(result.IsSuccess, result.Error?.Description);

    private static Result Untyped<T>(Result<T> result) => result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);

    private Task<TResult> AsAsync<TService, TResult>(UserId user, Func<TService, Task<TResult>> call)
        where TService : notnull =>
        Host.InScopeAsAsync(user, sp => call(sp.GetRequiredService<TService>()));

    private Task<Result<DisbursementStatus>> DisburseAsync(UserId user, LoanApplicationId id) =>
        AsAsync<DisbursementService, Result<DisbursementStatus>>(user, s => s.DisburseAsync(id, Ct));

    private Task<UserId> UserAsync(string userName) =>
        Host.QueryAsync(async db => new UserId(
            await db.UserDirectory.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(Ct)));

    private Task<ApplicationStatus> StatusAsync(LoanApplicationId id) =>
        Host.QueryAsync(db => db.LoanApplications.Where(a => a.Id == id).Select(a => a.Status).SingleAsync(Ct));

    private Task<string?> ContractNumberAsync(LoanApplicationId id) =>
        Host.QueryAsync(db => db.LoanApplications.Where(a => a.Id == id).Select(a => a.ContractNumber).SingleAsync(Ct));

    /// <summary>Approved by an underwriter (≤ 100,000 GEL) or the committee, created by the Vake operator.</summary>
    private async Task<LoanApplicationId> ApprovedAsync(decimal amount, ClientId? borrower = null, params ClientId[] guarantors)
    {
        var deciders = new[]
        {
            await UserAsync(amount > 100_000m ? "irakli.tsereteli" : "tamta.tatishvili"),
            await UserAsync("sophio.gogichaishvili"),
        };
        var product = amount > 50_000m ? LendingSeedCatalog.Mortgage : LendingSeedCatalog.Consumer;
        return await Lending.ApprovedAsync(product, amount, await UserAsync(VakeOperator), deciders, borrower, guarantors);
    }

    /// <summary>Approved, signed (through the service) and every condition met: ready for the payout.</summary>
    private async Task<LoanApplicationId> ReadyToDisburseAsync(decimal amount, ClientId? borrower = null, params ClientId[] guarantors)
    {
        var id = await ApprovedAsync(amount, borrower, guarantors);
        var operatorId = await UserAsync(VakeOperator);
        AssertSuccess(await AsAsync<ContractService, Result>(operatorId, s => s.SignAsync(id, Ct)));
        var conditions = await Host.QueryAsync(db => db.LoanApplications.Where(a => a.Id == id)
            .SelectMany(a => a.Conditions).Select(c => c.Id).ToListAsync(Ct));
        foreach (var condition in conditions)
        {
            AssertSuccess(await AsAsync<ContractService, Result>(operatorId, s => s.MarkConditionMetAsync(id, condition, Ct)));
        }

        Assert.Equal(ApplicationStatus.ConditionsMet, await StatusAsync(id));
        return id;
    }

    /// <summary>Exactly one payout and one core banking transaction for the application, with the same transaction id.</summary>
    private async Task<Disbursement> AssertPaidOnceAsync(LoanApplicationId id)
    {
        var payout = await Host.QueryAsync(db => db.Disbursements.SingleAsync(d => d.ApplicationId == id, Ct));
        var ledger = await Host.QueryAsync(db => db.Set<MockCoreBankingTransaction>().Where(t => t.IdempotencyKey == payout.IdempotencyKey).ToListAsync(Ct));
        Assert.Equal(DisbursementStatus.Succeeded, payout.Status);
        Assert.Equal(Assert.Single(ledger).TransactionId, payout.TransactionId);
        return payout;
    }
}
