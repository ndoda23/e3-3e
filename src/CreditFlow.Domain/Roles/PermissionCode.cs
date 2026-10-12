using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Roles;

/// <summary>
/// A permission code such as <c>underwriting.decide</c>: lowercase segments separated by dots,
/// each segment made of letters, digits and inner hyphens. Codes starting with <c>admin.</c> are administrative.
/// </summary>
public readonly record struct PermissionCode
{
    public const string AdministrativePrefix = "admin.";
    public const int MaxLength = 100;

    private PermissionCode(string value) => Value = value;

    public string Value { get; }

    /// <summary>True for administration permissions, which only administrative roles may hold.</summary>
    public bool IsAdministrative => Value.StartsWith(AdministrativePrefix, StringComparison.Ordinal);

    public static Result<PermissionCode> Create(string? value)
    {
        var code = Text.Normalize(value);
        return IsWellFormed(code)
            ? Result.Success(new PermissionCode(code))
            : Result.Failure<PermissionCode>(RoleErrors.InvalidPermission(code));
    }

    public override string ToString() => Value;

    private static bool IsWellFormed(string code)
    {
        if (code.Length is 0 or > MaxLength)
        {
            return false;
        }

        var segments = code.Split('.');
        return segments.Length >= 2 && segments.All(IsWellFormedSegment);
    }

    private static bool IsWellFormedSegment(string segment) =>
        segment.Length > 0
        && segment[0] != '-'
        && segment[^1] != '-'
        && segment.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
