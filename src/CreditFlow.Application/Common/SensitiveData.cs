using System.Text.RegularExpressions;

namespace CreditFlow.Application.Common;

/// <summary>
/// The single masking rule for logs and audit records. Personal numbers (any run of exactly 11 digits) keep only their
/// last four digits; values of secret-bearing fields (passwords, tokens, keys, recovery codes) are hidden completely,
/// and values of identifying fields (personal or document numbers) keep only their last four characters.
/// </summary>
public static partial class SensitiveData
{
    public const string Hidden = "***";

    private const int VisibleSuffix = 4;

    /// <summary>Field-name fragments whose values are never shown (case-insensitive).</summary>
    private static readonly string[] SecretFragments =
        ["password", "secret", "token", "authenticatorkey", "recoverycode", "securitystamp", "apikey"];

    /// <summary>Field-name fragments whose values show only the last four characters (case-insensitive).</summary>
    private static readonly string[] IdentifierFragments = ["personalnumber", "documentnumber"];

    /// <summary>Masks every personal number in free text (messages, exception text, serialized JSON).</summary>
    public static string MaskText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return PersonalNumberPattern().Replace(text, m => MaskSuffix(m.Value));
    }

    /// <summary>True when values of a field with this name must be masked.</summary>
    public static bool IsSensitiveField(string name) => IsSecret(name) || IsIdentifier(name);

    /// <summary>The masked value of a sensitive field (see <see cref="IsSensitiveField"/>).</summary>
    public static string MaskField(string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (value is null || IsSecret(name))
        {
            return Hidden;
        }

        return IsIdentifier(name) ? MaskSuffix(value) : MaskText(value);
    }

    private static bool IsSecret(string name) =>
        SecretFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));

    private static bool IsIdentifier(string name) =>
        IdentifierFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));

    private static string MaskSuffix(string value) =>
        value.Length <= VisibleSuffix
            ? new string('*', value.Length)
            : new string('*', value.Length - VisibleSuffix) + value[^VisibleSuffix..];

    /// <summary>Exactly 11 digits, not part of a longer number.</summary>
    [GeneratedRegex(@"(?<!\d)\d{11}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PersonalNumberPattern();
}
