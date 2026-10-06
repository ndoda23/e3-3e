using System.Collections.Frozen;
using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Lams.Web.TagHelpers;

/// <summary>
/// Renders a Lucide icon as inline SVG: <c>&lt;icon name="users" size="17" /&gt;</c>.
/// Decorative by default (<c>aria-hidden</c>); pass <c>label</c> for a meaningful icon.
/// Paths match the Lucide set used by the prototype (docs/lams-ui-reference/04-icons.jsx).
/// </summary>
[HtmlTargetElement("icon", TagStructure = TagStructure.WithoutEndTag)]
public sealed class IconTagHelper : TagHelper
{
    private static readonly FrozenDictionary<string, string> Paths = new Dictionary<string, string>
    {
        ["dashboard"] = """<rect x="3" y="3" width="7" height="9" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="12" width="7" height="9" rx="1.5"/><rect x="3" y="16" width="7" height="5" rx="1.5"/>""",
        ["files"] = """<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><path d="M14 3v6h6"/>""",
        ["inbox"] = """<polyline points="22 12 16 12 14 15 10 15 8 12 2 12"/><path d="M5.5 5h13l3 7v6a2 2 0 0 1-2 2h-15a2 2 0 0 1-2-2v-6z"/>""",
        ["bar-chart"] = """<line x1="4" y1="20" x2="20" y2="20"/><line x1="7" y1="20" x2="7" y2="12"/><line x1="12" y1="20" x2="12" y2="6"/><line x1="17" y1="20" x2="17" y2="14"/>""",
        ["settings"] = """<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1A1.7 1.7 0 0 0 4.6 9a1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>""",
        ["users"] = """<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>""",
        ["user-plus"] = """<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><line x1="19" y1="8" x2="19" y2="14"/><line x1="22" y1="11" x2="16" y2="11"/>""",
        ["user-check"] = """<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><polyline points="16 11 18 13 22 9"/>""",
        ["user-x"] = """<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><line x1="17" y1="8" x2="22" y2="13"/><line x1="22" y1="8" x2="17" y2="13"/>""",
        ["search"] = """<circle cx="11" cy="11" r="7"/><line x1="20" y1="20" x2="16.65" y2="16.65"/>""",
        ["chevron-down"] = """<polyline points="6 9 12 15 18 9"/>""",
        ["chevron-up"] = """<polyline points="18 15 12 9 6 15"/>""",
        ["chevron-right"] = """<polyline points="9 6 15 12 9 18"/>""",
        ["chevron-left"] = """<polyline points="15 6 9 12 15 18"/>""",
        ["plus"] = """<line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>""",
        ["x"] = """<line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>""",
        ["check"] = """<polyline points="20 6 9 17 4 12"/>""",
        ["eye"] = """<path d="M1 12s4-7 11-7 11 7 11 7-4 7-11 7-11-7-11-7z"/><circle cx="12" cy="12" r="3"/>""",
        ["more-h"] = """<circle cx="5" cy="12" r="1.4"/><circle cx="12" cy="12" r="1.4"/><circle cx="19" cy="12" r="1.4"/>""",
        ["shield-check"] = """<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><polyline points="9 12 11 14 15 10"/>""",
        ["shield-alert"] = """<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><line x1="12" y1="8" x2="12" y2="13"/><line x1="12" y1="16" x2="12" y2="16.5"/>""",
        ["alert-triangle"] = """<path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12" y2="17"/>""",
        ["building"] = """<rect x="4" y="3" width="16" height="18" rx="1.5"/><line x1="8" y1="7" x2="10" y2="7"/><line x1="14" y1="7" x2="16" y2="7"/><line x1="8" y1="11" x2="10" y2="11"/><line x1="14" y1="11" x2="16" y2="11"/><line x1="10" y1="21" x2="10" y2="17"/><line x1="14" y1="21" x2="14" y2="17"/>""",
        ["wallet"] = """<path d="M19 7H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2z"/><path d="M3 9V6a2 2 0 0 1 2-2h11"/><circle cx="17" cy="14" r="1"/>""",
        ["edit"] = """<path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.12 2.12 0 0 1 3 3L12 15l-4 1 1-4z"/>""",
        ["clock"] = """<circle cx="12" cy="12" r="9"/><polyline points="12 7 12 12 15 14"/>""",
        ["file-text"] = """<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><polyline points="14 3 14 9 20 9"/><line x1="8" y1="13" x2="16" y2="13"/><line x1="8" y1="17" x2="13" y2="17"/>""",
        ["refresh"] = """<polyline points="23 4 23 10 17 10"/><polyline points="1 20 1 14 7 14"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/>""",
        ["log-out"] = """<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/>""",
        ["lock"] = """<rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>""",
        ["unlock"] = """<rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 9.9-1"/>""",
        ["key"] = """<circle cx="7.5" cy="15.5" r="5.5"/><path d="m21 2-9.6 9.6"/><path d="m15.5 7.5 3 3L22 7l-3-3"/>""",
        ["smartphone"] = """<rect x="5" y="2" width="14" height="20" rx="2" ry="2"/><path d="M12 18h.01"/>""",
        ["history"] = """<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l4 2"/>""",
        ["sliders"] = """<line x1="21" y1="4" x2="14" y2="4"/><line x1="10" y1="4" x2="3" y2="4"/><line x1="21" y1="12" x2="12" y2="12"/><line x1="8" y1="12" x2="3" y2="12"/><line x1="21" y1="20" x2="16" y2="20"/><line x1="12" y1="20" x2="3" y2="20"/><line x1="14" y1="2" x2="14" y2="6"/><line x1="8" y1="10" x2="8" y2="14"/><line x1="16" y1="18" x2="16" y2="22"/>""",
        ["sparkles"] = """<path d="M12 3v3M12 18v3M3 12h3M18 12h3"/><path d="M6 6l2 2M18 18l-2-2M6 18l2-2M18 6l-2 2"/>""",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public string Name { get; set; } = string.Empty;

    public int Size { get; set; } = 18;

    /// <summary>Accessible label; when set the icon is announced, otherwise it is hidden from assistive technology.</summary>
    public string? Label { get; set; }

    public static bool Exists(string name) => Paths.ContainsKey(name);

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (!Paths.TryGetValue(Name, out var paths))
        {
            throw new InvalidOperationException($"Unknown icon '{Name}'.");
        }

        var size = Size.ToString(CultureInfo.InvariantCulture);
        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "icon");
        output.Attributes.SetAttribute("width", size);
        output.Attributes.SetAttribute("height", size);
        output.Attributes.SetAttribute("viewBox", "0 0 24 24");
        output.Attributes.SetAttribute("fill", "none");
        output.Attributes.SetAttribute("stroke", "currentColor");
        output.Attributes.SetAttribute("stroke-width", "1.6");
        output.Attributes.SetAttribute("stroke-linecap", "round");
        output.Attributes.SetAttribute("stroke-linejoin", "round");
        output.Attributes.SetAttribute("focusable", "false");

        if (string.IsNullOrWhiteSpace(Label))
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
        }
        else
        {
            output.Attributes.SetAttribute("role", "img");
            output.Attributes.SetAttribute("aria-label", Label);
        }

        output.Content.SetHtmlContent(paths);
    }
}
