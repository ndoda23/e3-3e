using Microsoft.Extensions.Localization;

namespace Lams.Web.Infrastructure;

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
}
