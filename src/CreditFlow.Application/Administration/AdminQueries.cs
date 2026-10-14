using CreditFlow.Application.Common;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;

namespace CreditFlow.Application.Administration;

/// <summary>A product in the admin list with the version in force.</summary>
public sealed record ProductAdminItem(
    LoanProductId Id, string Code, string Name, ProductType Type, bool IsActive, int CurrentVersion,
    DateTimeOffset CurrentVersionPublishedAt, ProductTerms CurrentTerms, int ApplicationCount);

/// <summary>A published version of a product.</summary>
public sealed record ProductVersionView(int Number, DateTimeOffset PublishedAt, ProductTerms Terms);

/// <summary>A product with every version, newest first.</summary>
public sealed record ProductAdminDetails(
    LoanProductId Id, string Code, string Name, ProductType Type, bool IsActive, IReadOnlyList<ProductVersionView> Versions);

/// <summary>A published scoring rule-set version.</summary>
public sealed record RuleSetVersionItem(int Version, DateTimeOffset PublishedAt, int RuleCount);

/// <summary>An FX rate row.</summary>
public sealed record FxRateItem(Currency Currency, DateOnly EffectiveDate, decimal GelPerUnit);

/// <summary>A public holiday.</summary>
public sealed record HolidayItem(DateOnly Date, string Name);

/// <summary>Read side of the lending reference data (Admin).</summary>
public interface ILendingAdminQueries
{
    Task<IReadOnlyList<ProductAdminItem>> ListProductsAsync(CancellationToken cancellationToken);

    Task<ProductAdminDetails?> GetProductAsync(LoanProductId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<RuleSetVersionItem>> ListRuleSetVersionsAsync(CancellationToken cancellationToken);

    /// <summary>FX rates, newest first, optionally for one currency (paged in SQL).</summary>
    Task<PagedResult<FxRateItem>> ListFxRatesAsync(Currency? currency, int page, CancellationToken cancellationToken);

    Task<IReadOnlyList<HolidayItem>> ListHolidaysAsync(int year, CancellationToken cancellationToken);
}
