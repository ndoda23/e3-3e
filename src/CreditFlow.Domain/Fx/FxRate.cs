using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Fx;

/// <summary>
/// The official rate of a foreign currency on a date: how many GEL one unit costs. Edited by the Admin or imported
/// from a file; the rate in effect on a date is the latest one on or before it.
/// </summary>
public sealed class FxRate
{
    /// <summary>Rates are kept with at most this many decimals.</summary>
    public const int RateDecimals = 6;

    private FxRate()
    {
    }

    public Currency Currency { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    /// <summary>GEL per one unit of <see cref="Currency"/>.</summary>
    public decimal GelPerUnit { get; private set; }

    public static Result<FxRate> Create(Currency currency, DateOnly effectiveDate, decimal gelPerUnit)
    {
        if (!Enum.IsDefined(currency) || currency == Currency.GEL)
        {
            return Result.Failure<FxRate>(FxErrors.InvalidCurrency);
        }

        var rate = new FxRate { Currency = currency, EffectiveDate = effectiveDate };
        var changed = rate.ChangeRate(gelPerUnit);
        return changed.IsSuccess ? Result.Success(rate) : Result.Failure<FxRate>(changed.Error!);
    }

    public Result ChangeRate(decimal gelPerUnit)
    {
        if (gelPerUnit <= 0m || decimal.Round(gelPerUnit, RateDecimals) != gelPerUnit)
        {
            return Result.Failure(FxErrors.InvalidRate);
        }

        GelPerUnit = gelPerUnit;
        return Result.Success();
    }

    public ExchangeRate ToExchangeRate() => new(Currency, GelPerUnit, EffectiveDate);
}

/// <summary>A rate used for one conversion. GEL converts to itself at 1.</summary>
public sealed record ExchangeRate(Currency Currency, decimal GelPerUnit, DateOnly EffectiveDate)
{
    public static ExchangeRate Gel(DateOnly date) => new(Currency.GEL, 1m, date);

    /// <summary>The GEL equivalent of an amount in this rate's currency (rounded with the system rule).</summary>
    public Money ToGel(Money amount)
    {
        EnsureCurrency(amount);
        return Money.Of(amount.Amount * GelPerUnit, Currency.GEL);
    }

    /// <summary>A GEL amount expressed in this rate's currency (rounded with the system rule).</summary>
    public Money FromGel(Money gel)
    {
        if (gel.Currency != Currency.GEL)
        {
            throw new InvalidOperationException($"Expected a GEL amount, got {gel.Currency}.");
        }

        return Money.Of(gel.Amount / GelPerUnit, Currency);
    }

    private void EnsureCurrency(Money amount)
    {
        if (amount.Currency != Currency)
        {
            throw new InvalidOperationException($"A {Currency} rate cannot convert {amount.Currency}.");
        }
    }
}

public static class FxErrors
{
    public static readonly Error InvalidCurrency = Error.Validation(
        "Fx.InvalidCurrency", "Rates are kept for foreign currencies (USD, EUR), not for GEL.", "Currency");

    public static readonly Error InvalidRate = Error.Validation(
        "Fx.InvalidRate", "The rate must be greater than zero with at most 6 decimals.", "GelPerUnit");

    public static readonly Error Duplicate = Error.Conflict(
        "Fx.Duplicate", "A rate for this currency and date already exists.", "EffectiveDate");

    public static Error RateMissing(Currency currency, DateOnly date) => Error.Conflict(
        "Fx.RateMissing", $"No {currency} rate is in effect on {date:yyyy-MM-dd}.");
}
