using Lams.Domain.Common;

namespace Lams.Infrastructure.Identity;

public static class AccountErrors
{
    public static readonly Error NotSignedIn = Error.Forbidden("Account.NotSignedIn", "You are not signed in.");

    public static readonly Error InvalidAuthenticatorCode = Error.Validation(
        "Account.InvalidAuthenticatorCode", "The verification code is not valid. Check the code in your app and try again.", "Code");

    public static readonly Error MfaRequired = Error.Conflict(
        "Account.MfaRequired", "Two-factor authentication is required for your account and cannot be turned off.");

    public static readonly Error MfaNotEnabled = Error.Conflict(
        "Account.MfaNotEnabled", "Two-factor authentication is not enabled for your account.");
}
