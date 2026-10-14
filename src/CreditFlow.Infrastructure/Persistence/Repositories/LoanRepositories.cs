using CreditFlow.Application.Disbursements;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Loans;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Repositories;

internal sealed class DisbursementRepository(CreditFlowDbContext db) : IDisbursementRepository
{
    public Task<Disbursement?> GetAsync(DisbursementId id, CancellationToken cancellationToken) =>
        db.Disbursements.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<Disbursement?> GetByApplicationAsync(LoanApplicationId applicationId, CancellationToken cancellationToken) =>
        db.Disbursements.FirstOrDefaultAsync(d => d.ApplicationId == applicationId, cancellationToken);

    public void Add(Disbursement disbursement) => db.Disbursements.Add(disbursement);
}

internal sealed class LoanRepository(CreditFlowDbContext db) : ILoanRepository
{
    public Task<Loan?> GetAsync(LoanId id, CancellationToken cancellationToken) =>
        WithParts().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<Loan?> GetByApplicationAsync(LoanApplicationId applicationId, CancellationToken cancellationToken) =>
        WithParts().FirstOrDefaultAsync(l => l.ApplicationId == applicationId, cancellationToken);

    public void Add(Loan loan) => db.Loans.Add(loan);

    private IQueryable<Loan> WithParts() =>
        db.Loans
            .Include(l => l.Installments.OrderBy(i => i.Number))
            .Include(l => l.Repayments.OrderBy(r => r.RecordedAt))
            .AsSplitQuery();
}

/// <summary>
/// Gapless contract numbers from one counter row per year. The row is created under a key-range lock if missing and
/// incremented with an <c>UPDATE … OUTPUT</c>, which keeps an exclusive lock until the caller's transaction ends:
/// parallel signings queue up, and a rolled-back signing returns its number to the counter.
/// </summary>
internal sealed class ContractNumberGenerator(CreditFlowDbContext db) : IContractNumberGenerator
{
    public async Task<int> NextAsync(int year, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Contract numbers are taken inside the signing transaction.");
        }

        var numbers = await db.Database.SqlQuery<int>($"""
            IF NOT EXISTS (SELECT 1 FROM ContractNumberSequences WITH (UPDLOCK, HOLDLOCK) WHERE [Year] = {year})
                INSERT INTO ContractNumberSequences ([Year], LastNumber) VALUES ({year}, 0);
            UPDATE ContractNumberSequences SET LastNumber = LastNumber + 1 OUTPUT inserted.LastNumber AS [Value] WHERE [Year] = {year};
            """).ToListAsync(cancellationToken);
        return numbers.Single();
    }
}

/// <summary>
/// <c>sp_getapplock</c> per client (<c>exposure:{id}</c>), exclusive, owned by the transaction (released on commit or
/// rollback), always taken in ascending id order so two payouts sharing parties cannot deadlock.
/// </summary>
internal sealed class SqlExposureLock(CreditFlowDbContext db) : IExposureLock
{
    private const int TimeoutMilliseconds = 15_000;

    public async Task AcquireAsync(IReadOnlyCollection<ClientId> clients, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clients);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Exposure locks are taken inside a transaction.");
        }

        foreach (var client in clients.Distinct().OrderBy(c => c.Value))
        {
            var resource = $"exposure:{client.Value:N}";
            var result = (await db.Database.SqlQuery<int>($"""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {TimeoutMilliseconds};
                SELECT @result AS [Value];
                """).ToListAsync(cancellationToken)).Single();
            if (result < 0)
            {
                throw new InvalidOperationException($"Could not lock the exposure of client {client} (sp_getapplock returned {result}).");
            }
        }
    }
}
