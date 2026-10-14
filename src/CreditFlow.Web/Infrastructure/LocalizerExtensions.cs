using Microsoft.Extensions.Localization;

namespace CreditFlow.Web.Infrastructure;

public static class LocalizerExtensions
{
    /// <summary>The localized text, or <paramref name="fallback"/> when the key has no resource.</summary>
    public static string TextOrDefault(this IStringLocalizer localizer, string key, string fallback)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var text = localizer[key];
        return text.ResourceNotFound ? fallback : text.Value;
    }

    /// <summary>
    /// Display name of an enum value (<c>Enum.{Type}.{Value}</c>); without a resource, the name split into words
    /// ("ComplianceReview" → "Compliance review").
    /// </summary>
    public static string EnumText<TEnum>(this IStringLocalizer localizer, TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        return localizer.TextOrDefault($"Enum.{typeof(TEnum).Name}.{name}", Words(name));
    }

    /// <summary>
    /// Display name of an audited action (<c>AuditAction.{action}</c>). Data changes are named with their entity
    /// (<c>AuditAction.{entityType}.{action}</c>, e.g. "User updated"). Unknown actions are shown as stored.
    /// </summary>
    public static string AuditAction(this IStringLocalizer localizer, string action, string? entityType)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(action);

        if (entityType is not null)
        {
            var specific = localizer[$"AuditAction.{entityType}.{action}"];
            if (!specific.ResourceNotFound)
            {
                return specific.Value;
            }
        }

        return localizer.TextOrDefault($"AuditAction.{action}", action);
    }

    private static string Words(string pascalCase)
    {
        var text = new System.Text.StringBuilder(pascalCase.Length + 8);
        foreach (var c in pascalCase)
        {
            text.Append(text.Length > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString());
        }

        return text.ToString();
    }
}
