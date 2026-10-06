using FluentValidation;
using FluentValidation.Results;
using Lams.Domain.Common;

namespace Lams.Application.Common;

internal static class ValidationExtensions
{
    /// <summary>Runs the validator and converts failures into domain errors (one per field failure).</summary>
    public static async Task<List<Error>> ValidateToErrorsAsync<T>(
        this IValidator<T> validator, T instance, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);
        return result.Errors.Select(ToError).ToList();
    }

    public static Result ToResult(this IReadOnlyList<Error> errors) =>
        errors.Count == 0 ? Result.Success() : Result.Failure(Error.ValidationFailed(errors));

    private static Error ToError(ValidationFailure failure) =>
        Error.Validation(
            string.IsNullOrEmpty(failure.ErrorCode) ? "Validation.Invalid" : failure.ErrorCode,
            failure.ErrorMessage,
            string.IsNullOrEmpty(failure.PropertyName) ? null : failure.PropertyName);
}
