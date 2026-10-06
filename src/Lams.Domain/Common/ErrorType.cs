namespace Lams.Domain.Common;

/// <summary>Category of an expected failure; the presentation layer maps it to a status code or a form error.</summary>
public enum ErrorType
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
}
