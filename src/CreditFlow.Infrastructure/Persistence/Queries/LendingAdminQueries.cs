using CreditFlow.Application.Administration;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Queries;

/// <summary>Reference-data lists for Admin (small tables; FX rates are paged in SQL).</summary>
internal sealed class LendingAdminQueries(CreditFlowDbContext db) : ILendingAdminQueries
{
    public async Task<IReadOnlyList<ProductAdminItem>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = await db.LoanProducts.AsNoTracking().Include(p => p.Versions).OrderBy(p => p.Name).ToListAsync(cancellationToken);
        var counts = await db.LoanApplications.AsNoTracking()
            .GroupBy(a => a.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Count, cancellationToken);

        return products.Select(p => new ProductAdminItem(
            p.Id, p.Code, p.Name, p.Type, p.IsActive, p.CurrentVersion.Number, p.CurrentVersion.PublishedAt, p.CurrentVersion.Terms,
            counts.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<ProductAdminDetails?> GetProductAsync(LoanProductId id, CancellationToken cancellationToken)
    {
        var product = await db.LoanProducts.AsNoTracking().Include(p => p.Versions).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return product is null
            ? null
            : new ProductAdminDetails(
                product.Id, product.Code, product.Name, product.Type, product.IsActive,
                product.Versions.OrderByDescending(v => v.Number).Select(v => new ProductVersionView(v.Number, v.PublishedAt, v.Terms)).ToList());
    }

    public async Task<IReadOnlyList<RuleSetVersionItem>> ListRuleSetVersionsAsync(CancellationToken cancellationToken) =>
        (await db.ScoringRuleSets.AsNoTracking().OrderByDescending(s => s.Version).ToListAsync(cancellationToken))
        .Select(s => new RuleSetVersionItem(s.Version, s.PublishedAt, s.Rules.Count))
        .ToList();

    public async Task<PagedResult<FxRateItem>> ListFxRatesAsync(Currency? currency, int page, CancellationToken cancellationToken)
    {
        var rates = db.FxRates.AsNoTracking();
        if (currency is { } wanted)
        {
            rates = rates.Where(r => r.Currency == wanted);
        }

        var total = await rates.CountAsync(cancellationToken);
        var items = await rates
            .OrderByDescending(r => r.EffectiveDate).ThenBy(r => r.Currency)
            .Skip((page - 1) * Paging.DefaultPageSize).Take(Paging.DefaultPageSize)
            .Select(r => new FxRateItem(r.Currency, r.EffectiveDate, r.GelPerUnit))
            .ToListAsync(cancellationToken);
        return new PagedResult<FxRateItem>(items, total, page, Paging.DefaultPageSize);
    }

    public async Task<IReadOnlyList<HolidayItem>> ListHolidaysAsync(int year, CancellationToken cancellationToken)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        return await db.Holidays.AsNoTracking()
            .Where(h => h.Date >= from && h.Date <= to)
            .OrderBy(h => h.Date)
            .Select(h => new HolidayItem(h.Date, h.Name))
            .ToListAsync(cancellationToken);
    }
}
