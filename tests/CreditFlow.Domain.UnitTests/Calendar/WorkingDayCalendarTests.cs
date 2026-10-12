using CreditFlow.Domain.Calendar;
using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.UnitTests.Calendar;

public sealed class WorkingDayCalendarTests
{
    // 2026-10-09 is a Friday.
    private static readonly DateOnly Friday = new(2026, 10, 9);
    private readonly WorkingDayCalendar _plain = new([]);

    [Fact]
    public void Weekends_are_not_working_days()
    {
        Assert.True(_plain.IsWorkingDay(Friday));
        Assert.False(_plain.IsWorkingDay(Friday.AddDays(1)));
        Assert.False(_plain.IsWorkingDay(Friday.AddDays(2)));
        Assert.True(_plain.IsWorkingDay(Friday.AddDays(3)));
    }

    [Fact]
    public void Two_working_days_from_Friday_is_Tuesday()
    {
        Assert.Equal(Friday.AddDays(4), _plain.AddWorkingDays(Friday, 2));
    }

    [Fact]
    public void Holidays_are_skipped()
    {
        var mondayHoliday = new WorkingDayCalendar([Friday.AddDays(3)]);

        Assert.False(mondayHoliday.IsWorkingDay(Friday.AddDays(3)));
        Assert.Equal(Friday.AddDays(5), mondayHoliday.AddWorkingDays(Friday, 2)); // Wednesday
    }

    [Fact]
    public void Starting_on_a_weekend_counts_from_the_next_working_day()
    {
        Assert.Equal(Friday.AddDays(4), _plain.AddWorkingDays(Friday.AddDays(1), 2)); // Saturday → Tuesday
    }

    [Fact]
    public void Zero_days_is_the_start_date()
    {
        Assert.Equal(Friday, _plain.AddWorkingDays(Friday, 0));
    }

    [Fact]
    public void Instants_keep_the_local_time_of_day_in_bank_time()
    {
        // Friday 10:00 in Tbilisi (06:00 UTC) + 2 working days = Tuesday 10:00 in Tbilisi.
        var start = new DateTimeOffset(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);

        var deadline = _plain.AddWorkingDays(start, 2);

        Assert.Equal(new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero), deadline.ToUniversalTime());
    }

    [Fact]
    public void Bank_dates_use_Tbilisi_time()
    {
        // 21:00 UTC on Friday is already Saturday 01:00 in Tbilisi.
        Assert.Equal(Friday.AddDays(1), BankTime.DateOf(new DateTimeOffset(2026, 10, 9, 21, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Holiday_names_are_required()
    {
        Assert.Equal(CalendarErrors.InvalidHolidayName, Holiday.Create(Friday, " ").Error);
        Assert.Equal("Independence Day", Holiday.Create(Friday, " Independence Day ").Value.Name);
    }
}
