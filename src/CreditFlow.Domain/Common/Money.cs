using System.Globalization;

namespace CreditFlow.Domain.Common;

/// <summary>
/// An amount of money in one currency. <b>The single rounding rule of the system lives here:</b> every amount is
/// rounded to <see cref="Decimals"/> decimals with <see cref="MidpointRounding.AwayFromZero"/> when a
/// <see cref="Money"/> is created, and intermediate calculations use <see cref="Round"/>. Only <c>decimal</c> is used.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public const int Decimals = 2;

    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    /// <summary>The system-wide rounding: 2 decimals, half away from zero.</summary>
    public static decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);

    /// <summary>The amount rounded with <see cref="Round"/>.</summary>
    public static Money Of(decimal amount, Currency currency)
    {
        if (!Enum.IsDefined(currency))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), currency, "Unknown currency.");
        }

        return new Money(Round(amount), currency);
    }

    public static Money Zero(Currency currency) => Of(0m, currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    /// <summary>This amount multiplied by <paramref name="factor"/>, rounded.</summary>
    public Money Multiply(decimal factor) => Of(Amount * factor, Currency);

    /// <summary>The given percentage of this amount (e.g. 1.5 for 1.5 %), rounded.</summary>
    public Money Percent(decimal percent) => Of(Amount * percent / 100m, Currency);

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot combine {left.Currency} and {right.Currency} amounts without a conversion.");
        }
    }
}
