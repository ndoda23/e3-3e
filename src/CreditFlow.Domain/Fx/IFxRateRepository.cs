using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Fx;

/// <summary>Persistence of <see cref="FxRate"/> rows.</summary>
public interface IFxRateRepository
{
    /// <summary>The rate in effect on a date: the latest one on or before it.</summary>
    Task<FxRate?> FindEffectiveAsync(Currency currency, DateOnly date, CancellationToken cancellationToken);

    Task<FxRate?> GetAsync(Currency currency, DateOnly effectiveDate, CancellationToken cancellationToken);

    void Add(FxRate rate);

    void Remove(FxRate rate);
}
