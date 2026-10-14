using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Roles;

/// <summary>Short, immutable business code of a role (e.g. <c>UW</c>). Uppercase letters and digits, 2–16 characters.</summary>
public readonly record struct RoleCode
{
    public const int MaxLength = 16;

    private RoleCode(string value) => Value = value;

    public string Value { get; }

    public static Result<RoleCode> Create(string? value)
    {
        var code = Text.Normalize(value).ToUpperInvariant();

        return code.Length is < 2 or > MaxLength || !code.All(char.IsAsciiLetterOrDigit)
            ? Result.Failure<RoleCode>(RoleErrors.InvalidCode)
            : Result.Success(new RoleCode(code));
    }

    public override string ToString() => Value;
}
