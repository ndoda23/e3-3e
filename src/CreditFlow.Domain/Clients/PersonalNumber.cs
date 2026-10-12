using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Clients;

/// <summary>A Georgian personal number: exactly 11 digits. Unique per client.</summary>
public readonly record struct PersonalNumber
{
    public const int Length = 11;

    private PersonalNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<PersonalNumber> Create(string? value)
    {
        var normalized = Text.Normalize(value);
        return normalized.Length == Length && normalized.All(char.IsAsciiDigit)
            ? Result.Success(new PersonalNumber(normalized))
            : Result.Failure<PersonalNumber>(ClientErrors.InvalidPersonalNumber);
    }

    public override string ToString() => Value;
}
