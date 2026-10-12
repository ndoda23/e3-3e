using System.Collections.Concurrent;
using CreditFlow.Application.Common;
using CreditFlow.Application.Disbursements;
using CreditFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.Infrastructure.ExternalServices;

/// <summary>A transfer the mock core banking executed (its own "ledger"; the idempotency key is the primary key).</summary>
public sealed class MockCoreBankingTransaction
{
    public string IdempotencyKey { get; set; } = string.Empty;

    public string TransactionId { get; set; } = string.Empty;

    public string Beneficiary { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public DateTimeOffset ExecutedAt { get; set; }
}

/// <summary>
/// The core banking mock. Every key is executed at most once: the ledger row is written in the mock's own scope (its
/// own connection and transaction, like a separate system), and a repeated key returns the original transaction id.
/// Scenarios by the borrower's personal number: <see cref="MockScenario.CoreBankingError"/> rejects the first request
/// of a key (no money moved); <see cref="MockScenario.CoreBankingTimeout"/> executes the first request but answers too
/// late, so the caller sees a timeout and must retry with the same key.
/// </summary>
internal sealed class MockCoreBankingClient(MockScenarios scenarios, IServiceScopeFactory scopes, TimeProvider clock)
    : ICoreBankingClient
{
    /// <summary>Requests per key (the mock is a singleton per host).</summary>
    private readonly ConcurrentDictionary<string, int> _requests = new(StringComparer.Ordinal);

    public async Task<CoreBankingResult> TransferAsync(CoreBankingTransfer transfer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        var request = _requests.AddOrUpdate(transfer.IdempotencyKey, 1, (_, n) => n + 1);
        var beneficiary = transfer.BeneficiaryPersonalNumber;

        if (request == 1 && scenarios.Has(beneficiary, MockScenario.CoreBankingError))
        {
            return CoreBankingResult.Rejected("The beneficiary account is blocked (mock core banking).");
        }

        var transactionId = await ExecuteOnceAsync(transfer, cancellationToken);

        if (request == 1 && scenarios.Has(beneficiary, MockScenario.CoreBankingTimeout))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(scenarios.Options.CoreBankingTimeout);
            try
            {
                await Task.Delay(scenarios.Options.HangDuration, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ExternalServiceUnavailableException("Core banking did not answer in time.");
            }
        }

        return CoreBankingResult.Success(transactionId);
    }

    private async Task<string> ExecuteOnceAsync(CoreBankingTransfer transfer, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CreditFlowDbContext>();
        var ledger = db.Set<MockCoreBankingTransaction>();

        var existing = await ledger.AsNoTracking().FirstOrDefaultAsync(t => t.IdempotencyKey == transfer.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return existing.TransactionId;
        }

        ledger.Add(new MockCoreBankingTransaction
        {
            IdempotencyKey = transfer.IdempotencyKey,
            TransactionId = $"CB{Guid.CreateVersion7():N}".ToUpperInvariant(),
            Beneficiary = transfer.BeneficiaryPersonalNumber,
            Amount = transfer.Amount.Amount,
            Currency = transfer.Amount.Currency.ToString(),
            Reference = transfer.Reference,
            ExecutedAt = clock.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same key arrived in parallel and won: return its transaction.
            db.ChangeTracker.Clear();
        }

        return await ledger.AsNoTracking()
            .Where(t => t.IdempotencyKey == transfer.IdempotencyKey)
            .Select(t => t.TransactionId)
            .SingleAsync(cancellationToken);
    }
}
