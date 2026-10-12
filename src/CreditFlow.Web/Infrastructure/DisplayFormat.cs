using System.Globalization;
using CreditFlow.Domain.Common;

namespace CreditFlow.Web.Infrastructure;

/// <summary>Formatting of dates and amounts shown in views.</summary>
public static class DisplayFormat
{
    public const string Empty = "—";

    /// <summary>Local date and short time (current UI culture), or a dash.</summary>
    public static string DateTime(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? Empty;

    /// <summary>An amount with its currency symbol and no decimals, e.g. <c>₾50,000</c>.</summary>
    public static string Money(Currency currency, decimal amount) =>
        CurrencySymbol(currency) + amount.ToString("#,0.##", CultureInfo.InvariantCulture);

    public static string CurrencySymbol(Currency currency) => currency switch
    {
        Currency.GEL => "₾",
        Currency.USD => "$",
        Currency.EUR => "€",
        _ => currency + " ",
    };
}
