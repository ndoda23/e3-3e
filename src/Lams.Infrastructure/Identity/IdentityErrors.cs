using Lams.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace Lams.Infrastructure.Identity;

internal static class IdentityErrors
{
    public static readonly Error AccountNotFound = Error.NotFound("User.AccountNotFound", "The user account was not found.");

    /// <summary>Converts Identity errors to domain errors (code <c>Identity.{Code}</c>, field derived from the code).</summary>
    public static Result ToResult(this IdentityResult result)
    {
        if (result.Succeeded)
        {
            return Result.Success();
        }

        var errors = result.Errors
            .Select(e => Error.Validation($"Identity.{e.Code}", e.Description, TargetOf(e.Code)))
            .ToList();
        return Result.Failure(Error.ValidationFailed(errors));
    }

    private static string? TargetOf(string code) => code switch
    {
        "PasswordMismatch" => "CurrentPassword",
        _ when code.StartsWith("Password", StringComparison.Ordinal) => "Password",
        "DuplicateUserName" or "InvalidUserName" => "UserName",
        "DuplicateEmail" or "InvalidEmail" => "Email",
        _ => null,
    };
}
