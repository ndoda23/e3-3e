namespace CreditFlow.Domain.Calendar;

/// <summary>Persistence of <see cref="Holiday"/> rows.</summary>
public interface IHolidayRepository
{
    Task<Holiday?> GetAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>The holiday dates in a range (both inclusive), for a <see cref="WorkingDayCalendar"/>.</summary>
    Task<IReadOnlyList<DateOnly>> ListDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    void Add(Holiday holiday);

    void Remove(Holiday holiday);
}
