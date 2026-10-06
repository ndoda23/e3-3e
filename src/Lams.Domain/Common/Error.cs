namespace Lams.Domain.Common;

/// <summary>
/// An expected failure. <see cref="Code"/> is stable and is used as the localization key
/// (<c>Error.{Code}</c>); <see cref="Description"/> is the English fallback text.
/// <see cref="Target"/> optionally names the input field the error belongs to.
/// </summary>
public sealed record Error(string Code, string Description, ErrorType Type, string? Target = null)
{
    public const string ValidationFailedCode = "Validation.Failed";

    /// <summary>Individual field errors when this error aggregates several validation failures.</summary>
    public IReadOnlyList<Error> Details { get; init; } = [];

    /// <summary>Several field errors reported together, so a form can show all of them at once.</summary>
    public static Error ValidationFailed(IReadOnlyList<Error> details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return details.Count == 1
            ? details[0]
            : new Error(ValidationFailedCode, "One or more fields are not valid.", ErrorType.Validation)
            {
                Details = details,
            };
    }

    public static Error Validation(string code, string description, string? target = null) =>
        new(code, description, ErrorType.Validation, target);

    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description, string? target = null) =>
        new(code, description, ErrorType.Conflict, target);

    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);
}
