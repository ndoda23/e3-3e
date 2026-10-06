namespace Lams.Domain.Common;

/// <summary>Small helpers for validating text input in factories.</summary>
internal static class Text
{
    /// <summary>Trims the value; returns an empty string for <c>null</c>.</summary>
    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
