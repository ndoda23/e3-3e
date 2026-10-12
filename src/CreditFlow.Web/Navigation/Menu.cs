using CreditFlow.Application.Authorization;
using CreditFlow.Domain.Roles;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Navigation;

/// <summary>A sidebar entry. Visibility is decided by permissions (never role names).</summary>
/// <param name="Key">Stable key; also the resource key suffix (<c>Nav.{Key}</c>).</param>
/// <param name="Area">MVC area ("" for none).</param>
/// <param name="Controller">MVC controller.</param>
/// <param name="Icon">Icon name (<c>&lt;icon&gt;</c> tag helper).</param>
/// <param name="IsVisible">Whether the user may see the entry.</param>
public sealed record MenuItem(string Key, string Area, string Controller, string Icon, Func<UserAccessProfile, bool> IsVisible);

public sealed record MenuSection(string Key, IReadOnlyList<MenuItem> Items);

/// <summary>The single definition of the sidebar (FRD shell + Phase 1 admin pages).</summary>
public static class Menu
{
    public static IReadOnlyList<MenuItem> Workspace { get; } =
    [
        new("Dashboard", "", "Dashboard", "dashboard", p => p.IsActive && p.Dashboard != DashboardKind.Administration),
        new("Applications", "", "Applications", "files",
            p => p.HasAnyPermission(P.Applications.ViewFull, P.Aml.ViewScreening, P.Pricing.ViewExceptions)),
        new("Packages", "", "Packages", "inbox", p => p.HasPermission(P.Applications.ViewFull)),
        new("Reports", "", "Reports", "bar-chart", p => p.HasPermission(P.Reports.View)),
    ];

    public static IReadOnlyList<MenuItem> Administration { get; } =
    [
        new("Users", "Admin", "Users", "users", p => p.HasPermission(P.Admin.UsersView)),
        new("Roles", "Admin", "Roles", "shield-check", p => p.HasPermission(P.Admin.RolesManage)),
        new("AuthorityLimits", "Admin", "AuthorityLimits", "sliders", p => p.HasPermission(P.Admin.AuthorityLimitsManage)),
        new("Branches", "Admin", "Branches", "building", p => p.HasPermission(P.Admin.BranchesManage)),
        new("AuditLog", "Admin", "AuditLog", "history", p => p.HasPermission(P.Admin.AuditView)),
        new("CreditPolicy", "Admin", "CreditPolicy", "shield-alert", p => p.HasPermission(P.Admin.CreditPolicyManage)),
        new("Pricing", "Admin", "Pricing", "wallet", p => p.HasPermission(P.Admin.PricingManage)),
        new("Settings", "Admin", "Settings", "settings", p => p.HasPermission(P.Admin.SettingsManage)),
    ];

    /// <summary>The sections and entries the user may see; sections without visible entries are left out.</summary>
    public static IReadOnlyList<MenuSection> For(UserAccessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var sections = new List<MenuSection>
        {
            new("Workspace", Workspace.Where(i => i.IsVisible(profile)).ToList()),
            new("Administration", Administration.Where(i => i.IsVisible(profile)).ToList()),
        };
        return sections.Where(s => s.Items.Count > 0).ToList();
    }

    /// <summary>The topbar "Create Application" button.</summary>
    public static bool CanCreateApplication(UserAccessProfile profile) =>
        profile?.HasPermission(P.Applications.Create) == true;
}
