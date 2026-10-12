using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Calendar;

/// <summary>A bank holiday (a non-working day besides weekends). Edited by the Admin.</summary>
public sealed class Holiday
{
    public const int NameMaxLength = 100;

    private Holiday()
    {
    }

    public DateOnly Date { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public static Result<Holiday> Create(DateOnly date, string? name)
    {
        var holiday = new Holiday { Date = date };
        var renamed = holiday.Rename(name);
        return renamed.IsSuccess ? Result.Success(holiday) : Result.Failure<Holiday>(renamed.Error!);
    }

    public Result Rename(string? name)
    {
        var normalized = Text.Normalize(name);
        if (normalized.Length is 0 or > NameMaxLength)
        {
            return Result.Failure(CalendarErrors.InvalidHolidayName);
        }

        Name = normalized;
        return Result.Success();
    }
}

public static class CalendarErrors
{
    public static readonly Error InvalidHolidayName = Error.Validation(
        "Holiday.InvalidName", "A holiday name is required (up to 100 characters).", "Name");

    public static readonly Error DuplicateHoliday = Error.Conflict(
        "Holiday.Duplicate", "This date is already a holiday.", "Date");

    public static readonly Error HolidayNotFound = Error.NotFound("Holiday.NotFound", "The holiday was not found.");
}
