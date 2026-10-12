using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Calendar;

/// <summary>
/// Working days of the bank: Monday to Friday, except holidays. Used for the escalation deadline (an application
/// that stays in Underwriting for more than two working days is escalated).
/// </summary>
public sealed class WorkingDayCalendar
{
    private readonly HashSet<DateOnly> _holidays;

    public WorkingDayCalendar(IEnumerable<DateOnly> holidays)
    {
        ArgumentNullException.ThrowIfNull(holidays);
        _holidays = holidays.ToHashSet();
    }

    public bool IsWorkingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !_holidays.Contains(date);

    /// <summary>The date <paramref name="days"/> working days after <paramref name="start"/> (start itself not counted).</summary>
    public DateOnly AddWorkingDays(DateOnly start, int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);

        var date = start;
        for (var added = 0; added < days;)
        {
            date = date.AddDays(1);
            if (IsWorkingDay(date))
            {
                added++;
            }
        }

        return date;
    }

    /// <summary>
    /// The instant <paramref name="days"/> working days after <paramref name="start"/>, at the same local time of day
    /// (bank time). Entering on Friday 10:00 with 2 days gives Tuesday 10:00; entering on Saturday gives Tuesday too.
    /// </summary>
    public DateTimeOffset AddWorkingDays(DateTimeOffset start, int days)
    {
        var local = BankTime.ToLocal(start);
        var date = AddWorkingDays(DateOnly.FromDateTime(local.DateTime), days);
        return BankTime.ToInstant(date, TimeOnly.FromDateTime(local.DateTime));
    }
}
