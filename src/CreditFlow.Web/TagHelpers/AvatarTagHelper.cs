using Microsoft.AspNetCore.Razor.TagHelpers;

namespace CreditFlow.Web.TagHelpers;

/// <summary>
/// Initials avatar: <c>&lt;avatar name="Tamta Tatishvili" size="sm" /&gt;</c>. The colour is a CSS class derived
/// from the name (no inline styles: CSP).
/// </summary>
[HtmlTargetElement("avatar", TagStructure = TagStructure.WithoutEndTag)]
public sealed class AvatarTagHelper : TagHelper
{
    private const int HueCount = 8;

    public string Name { get; set; } = string.Empty;

    /// <summary><c>sm</c>, <c>lg</c> or empty for the default size.</summary>
    public string? Size { get; set; }

    public static string Initials(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[^1][0])),
        };
    }

    public static int Hue(string? name)
    {
        // Stable across processes (string.GetHashCode is randomized per process).
        var hash = 17;
        foreach (var c in name ?? string.Empty)
        {
            hash = unchecked((hash * 31) + c);
        }

        return Math.Abs(hash % HueCount);
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        var size = string.IsNullOrWhiteSpace(Size) ? string.Empty : $" {Size}";
        output.Attributes.SetAttribute("class", $"avatar{size} h{Hue(Name)}");
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Content.SetContent(Initials(Name));
    }
}
