namespace CreditFlow.Domain.Common;

/// <summary>
/// The bank's local time (Asia/Tbilisi, UTC+4, no daylight saving). Instants are stored in UTC; business dates
/// (birthdays, document expiry, holidays, working days, contract years) are dates in this time zone.
/// </summary>
public static class BankTime
{
    public const string TimeZoneId = "Asia/Tbilisi";

    public static TimeZoneInfo Zone { get; } = Resolve();

    public static DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    /// <summary>The bank's calendar date at the given instant.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant).DateTime);

    /// <summary>The instant of a local date and time of day.</summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Hosts without time zone data: Georgia has used UTC+4 without daylight saving since 2005.
            return TimeZoneInfo.CreateCustomTimeZone(TimeZoneId, TimeSpan.FromHours(4), "Tbilisi", "Tbilisi");
        }
    }
}
