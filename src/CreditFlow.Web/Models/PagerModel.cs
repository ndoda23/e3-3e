namespace CreditFlow.Web.Models;

/// <summary>
/// Paging links for a list: each link is a normal URL (works without JavaScript) and, with htmx, loads only the list
/// body (<paramref name="PartialUrl"/>) into <paramref name="Target"/>.
/// </summary>
/// <param name="Page">Current page (1-based).</param>
/// <param name="TotalPages">Number of pages (at least 1).</param>
/// <param name="TotalCount">Number of items over all pages.</param>
/// <param name="PageSize">Items per page.</param>
/// <param name="PageUrl">URL of the full page for a page number.</param>
/// <param name="PartialUrl">URL of the list body for a page number.</param>
/// <param name="Target">CSS selector of the element the list body replaces.</param>
public sealed record PagerModel(
    int Page,
    int TotalPages,
    int TotalCount,
    int PageSize,
    Func<int, string> PageUrl,
    Func<int, string> PartialUrl,
    string Target)
{
    private const int Window = 2;

    public int FirstItem => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastItem => Math.Min(Page * PageSize, TotalCount);

    /// <summary>Page numbers to show: first, last and a window around the current page; <c>null</c> marks a gap.</summary>
    public IReadOnlyList<int?> Pages()
    {
        var pages = new List<int?>();
        for (var p = 1; p <= TotalPages; p++)
        {
            if (p == 1 || p == TotalPages || Math.Abs(p - Page) <= Window)
            {
                pages.Add(p);
            }
            else if (pages.Count > 0 && pages[^1] is not null)
            {
                pages.Add(null);
            }
        }

        return pages;
    }
}
