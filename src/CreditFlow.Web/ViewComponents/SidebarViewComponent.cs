using CreditFlow.Web.Authorization;
using CreditFlow.Web.Navigation;
using Microsoft.AspNetCore.Mvc;

namespace CreditFlow.Web.ViewComponents;

public sealed record SidebarModel(IReadOnlyList<MenuSection> Sections, string CurrentArea, string CurrentController);

/// <summary>Renders the sidebar navigation, filtered by the user's permissions.</summary>
public sealed class SidebarViewComponent(CurrentAccess currentAccess) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var profile = await currentAccess.GetAsync();
        var sections = profile is null ? [] : Menu.For(profile);

        return View(new SidebarModel(
            sections,
            RouteData.Values["area"] as string ?? string.Empty,
            RouteData.Values["controller"] as string ?? string.Empty));
    }
}

/// <summary>The user card at the bottom of the sidebar: name, role, branch and Sign out.</summary>
public sealed class UserCardViewComponent(CurrentAccess currentAccess) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() => View(await currentAccess.GetAsync());
}
