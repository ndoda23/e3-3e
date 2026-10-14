using System.Text;
using System.Text.RegularExpressions;

namespace CreditFlow.Application.Common;

/// <summary>
/// The single masking rule for logs and audit records:
/// <list type="bullet">
/// <item>personal numbers (any run of exactly 11 digits) keep only their last four digits;</item>
/// <item>email addresses keep the first character and the domain (<c>a***@pcb.ge</c>);</item>
/// <item>phone numbers (international <c>+995 599 011011</c>, or a Georgian mobile <c>599 01 10 11</c>) keep only their
/// last two digits;</item>
/// <item>values of secret-bearing fields (passwords, tokens, keys, recovery codes) are hidden completely; values of
/// identifying fields (personal or document numbers) keep their last four characters; values of contact fields
/// (phone, email, recipient) are masked as above even when they do not look like a phone or an address.</item>
/// </list>
/// </summary>
public static partial class SensitiveData
{
    public const string Hidden = "***";

    private const int VisibleSuffix = 4;
    private const int VisiblePhoneDigits = 2;

    /// <summary>Field-name fragments whose values are never shown (case-insensitive).</summary>
    private static readonly string[] SecretFragments =
        ["password", "secret", "token", "authenticatorkey", "recoverycode", "securitystamp", "apikey"];

    /// <summary>Field-name fragments whose values show only the last four characters (case-insensitive).</summary>
    private static readonly string[] IdentifierFragments = ["personalnumber", "documentnumber"];

    /// <summary>Field-name fragments of contact data (case-insensitive).</summary>
    private static readonly string[] ContactFragments = ["phone", "email", "recipient"];

    /// <summary>Masks personal numbers, email addresses and phone numbers in free text (messages, exceptions, JSON).</summary>
    public static string MaskText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var masked = EmailPattern().Replace(text, m => MaskEmail(m.Value));
        masked = PersonalNumberPattern().Replace(masked, m => MaskSuffix(m.Value));
        masked = InternationalPhonePattern().Replace(masked, m => MaskPhone(m.Value));
        return GeorgianMobilePattern().Replace(masked, m => MaskPhone(m.Value));
    }

    /// <summary>True when values of a field with this name must be masked.</summary>
    public static bool IsSensitiveField(string name) => IsSecret(name) || IsIdentifier(name) || IsContact(name);

    /// <summary>The masked value of a sensitive field (see <see cref="IsSensitiveField"/>).</summary>
    public static string MaskField(string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (value is null || IsSecret(name))
        {
            return Hidden;
        }

        if (IsIdentifier(name))
        {
            return MaskSuffix(value);
        }

        if (IsContact(name))
        {
            // A contact value that matches no pattern (an unusual format) is still not shown in clear.
            var masked = MaskText(value);
            return masked == value && value.Length > 0 ? MaskPhone(value) : masked;
        }

        return MaskText(value);
    }

    private static bool IsSecret(string name) =>
        SecretFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));

    private static bool IsIdentifier(string name) =>
        IdentifierFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));

    private static bool IsContact(string name) =>
        ContactFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));

    private static string MaskSuffix(string value) =>
        value.Length <= VisibleSuffix
            ? new string('*', value.Length)
            : new string('*', value.Length - VisibleSuffix) + value[^VisibleSuffix..];

    /// <summary><c>anna.g@pcb.ge</c> → <c>a***@pcb.ge</c>.</summary>
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return email[0] + Hidden + email[at..];
    }

    /// <summary>Every character except the last two digits becomes <c>*</c>, the separators and a leading <c>+</c> stay.</summary>
    private static string MaskPhone(string phone)
    {
        var digits = phone.Count(char.IsAsciiDigit);
        var shown = 0;
        var result = new StringBuilder(phone.Length);
        for (var i = phone.Length - 1; i >= 0; i--)
        {
            var c = phone[i];
            if (char.IsAsciiDigit(c) && shown < VisiblePhoneDigits && digits > VisiblePhoneDigits)
            {
                shown++;
                result.Insert(0, c);
            }
            else
            {
                result.Insert(0, char.IsAsciiDigit(c) || char.IsLetter(c) ? '*' : c);
            }
        }

        return result.ToString();
    }

    /// <summary>Exactly 11 digits, not part of a longer number.</summary>
    [GeneratedRegex(@"(?<!\d)\d{11}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PersonalNumberPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    /// <summary>A <c>+</c>, a country code and 7–14 more digits, with spaces, dashes, dots or brackets between them.</summary>
    [GeneratedRegex(@"\+\d{1,3}(?:[ \-.()]{0,2}\d){7,14}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex InternationalPhonePattern();

    /// <summary>A Georgian mobile number without the country code: 5XX XX XX XX (separators optional).</summary>
    [GeneratedRegex(@"(?<![\w*+])5\d{2}(?:[ \-]?\d{2}){3}(?!\w)", RegexOptions.CultureInvariant)]
    private static partial Regex GeorgianMobilePattern();
}
