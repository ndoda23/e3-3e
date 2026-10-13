using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Calendar;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Repositories;

internal sealed class ClientRepository(CreditFlowDbContext db) : IClientRepository
{
    public Task<Client?> GetAsync(ClientId id, CancellationToken cancellationToken) =>
        WithParts().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<ClientId, Client>> GetManyAsync(
        IEnumerable<ClientId> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        return await WithParts().Where(c => wanted.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
    }

    public Task<bool> PersonalNumberExistsAsync(PersonalNumber personalNumber, CancellationToken cancellationToken) =>
        db.Clients.AnyAsync(c => c.PersonalNumber == personalNumber, cancellationToken);

    public void Add(Client client) => db.Clients.Add(client);

    private IQueryable<Client> WithParts() =>
        db.Clients.Include(c => c.Incomes).Include(c => c.Obligations).AsSplitQuery();
}

internal sealed class LoanProductRepository(CreditFlowDbContext db) : ILoanProductRepository
{
    public Task<LoanProduct?> GetAsync(LoanProductId id, CancellationToken cancellationToken) =>
        db.LoanProducts.Include(p => p.Versions).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        db.LoanProducts.AnyAsync(p => p.Code == code, cancellationToken);

    public void Add(LoanProduct product) => db.LoanProducts.Add(product);
}

internal sealed class LoanApplicationRepository(CreditFlowDbContext db) : ILoanApplicationRepository
{
    /// <summary>The whole aggregate; ordered parts, one query per collection (no cartesian explosion).</summary>
    public Task<LoanApplication?> GetAsync(LoanApplicationId id, CancellationToken cancellationToken) =>
        db.LoanApplications
            .Include(a => a.Parties)
            .Include(a => a.Collaterals)
            .Include(a => a.Documents.OrderBy(d => d.UploadedAt))
            .Include(a => a.History.OrderBy(h => h.Id))
            .Include(a => a.Decisions.OrderBy(d => d.DecidedAt))
            .Include(a => a.Conditions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<bool> HasOpenApplicationAsync(
        ClientId borrowerId, LoanProductId productId, LoanApplicationId except, CancellationToken cancellationToken) =>
        db.LoanApplications.AnyAsync(
            a => a.BorrowerId == borrowerId && a.ProductId == productId && a.IsOpen && a.Id != except,
            cancellationToken);

    public async Task<string?> FindOpenApplicationReferenceAsync(
        ClientId borrowerId, LoanProductId productId, CancellationToken cancellationToken)
    {
        var number = await db.LoanApplications
            .Where(a => a.BorrowerId == borrowerId && a.ProductId == productId && a.IsOpen)
            .OrderBy(a => a.Number)
            .Select(a => (long?)a.Number)
            .FirstOrDefaultAsync(cancellationToken);
        return number is { } value ? LoanApplication.FormatReference(value) : null;
    }

    public void Add(LoanApplication application) => db.LoanApplications.Add(application);
}

internal sealed class ScoringRuleSetRepository(CreditFlowDbContext db) : IScoringRuleSetRepository
{
    public Task<ScoringRuleSet?> GetLatestAsync(CancellationToken cancellationToken) =>
        db.ScoringRuleSets.OrderByDescending(s => s.Version).FirstOrDefaultAsync(cancellationToken);

    public Task<ScoringRuleSet?> GetAsync(int version, CancellationToken cancellationToken) =>
        db.ScoringRuleSets.FirstOrDefaultAsync(s => s.Version == version, cancellationToken);

    public void Add(ScoringRuleSet ruleSet) => db.ScoringRuleSets.Add(ruleSet);
}

internal sealed class ApprovalThresholdsRepository(CreditFlowDbContext db) : IApprovalThresholdsRepository
{
    /// <summary>The single row; seeded, so it always exists.</summary>
    public Task<ApprovalThresholds> GetAsync(CancellationToken cancellationToken) =>
        db.ApprovalThresholds.SingleAsync(cancellationToken);
}

internal sealed class FxRateRepository(CreditFlowDbContext db) : IFxRateRepository
{
    public Task<FxRate?> FindEffectiveAsync(Currency currency, DateOnly date, CancellationToken cancellationToken) =>
        db.FxRates
            .Where(r => r.Currency == currency && r.EffectiveDate <= date)
            .OrderByDescending(r => r.EffectiveDate)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<FxRate?> GetAsync(Currency currency, DateOnly effectiveDate, CancellationToken cancellationToken) =>
        db.FxRates.FirstOrDefaultAsync(r => r.Currency == currency && r.EffectiveDate == effectiveDate, cancellationToken);

    public void Add(FxRate rate) => db.FxRates.Add(rate);

    public void Remove(FxRate rate) => db.FxRates.Remove(rate);
}

internal sealed class HolidayRepository(CreditFlowDbContext db) : IHolidayRepository
{
    public Task<Holiday?> GetAsync(DateOnly date, CancellationToken cancellationToken) =>
        db.Holidays.FirstOrDefaultAsync(h => h.Date == date, cancellationToken);

    public async Task<IReadOnlyList<DateOnly>> ListDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Holidays.AsNoTracking()
            .Where(h => h.Date >= from && h.Date <= to)
            .OrderBy(h => h.Date)
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);

    public void Add(Holiday holiday) => db.Holidays.Add(holiday);

    public void Remove(Holiday holiday) => db.Holidays.Remove(holiday);
}
