using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Lams.Web.Authorization;

/// <summary>
/// Renders the element only when the signed-in user holds the permission:
/// <c>&lt;a asp-permission="applications.create" …&gt;</c>, or any of several:
/// <c>&lt;li asp-any-permission="admin.users.view,admin.roles.manage"&gt;</c>.
/// This hides UI only; the server still authorizes every request.
/// </summary>
[HtmlTargetElement(Attributes = PermissionAttribute)]
[HtmlTargetElement(Attributes = AnyPermissionAttribute)]
public sealed class PermissionTagHelper(CurrentAccess currentAccess) : TagHelper
{
    private const string PermissionAttribute = "asp-permission";
    private const string AnyPermissionAttribute = "asp-any-permission";

    /// <summary>Runs before other tag helpers so suppressed content does no further work.</summary>
    public override int Order => -1000;

    [HtmlAttributeName(PermissionAttribute)]
    public string? Permission { get; set; }

    [HtmlAttributeName(AnyPermissionAttribute)]
    public string? AnyPermission { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var required = new List<string>();
        if (!string.IsNullOrWhiteSpace(Permission))
        {
            required.Add(Permission.Trim());
        }

        if (!string.IsNullOrWhiteSpace(AnyPermission))
        {
            required.AddRange(AnyPermission.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var profile = await currentAccess.GetAsync();
        if (profile is null || required.Count == 0 || !required.Any(profile.HasPermission))
        {
            output.SuppressOutput();
        }
    }
}
