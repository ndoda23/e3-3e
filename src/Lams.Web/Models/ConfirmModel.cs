namespace Lams.Web.Models;

/// <summary>
/// A confirmation dialog for a POST action: shown as a modal (htmx) or as a page without JavaScript
/// (<c>Views/Shared/_ConfirmModal</c>, <c>Views/Shared/Confirm</c>).
/// </summary>
/// <param name="Title">Dialog title (a question).</param>
/// <param name="Subject">What the action applies to (shown under the title).</param>
/// <param name="Description">Consequences of the action.</param>
/// <param name="ActionUrl">URL the confirm button posts to.</param>
/// <param name="CancelUrl">Where Cancel goes without JavaScript.</param>
/// <param name="ButtonText">Text of the confirm button.</param>
/// <param name="Icon">Icon name.</param>
/// <param name="IsDangerous">Red styling for destructive actions.</param>
public sealed record ConfirmModel(
    string Title,
    string Subject,
    string Description,
    string ActionUrl,
    string CancelUrl,
    string ButtonText,
    string Icon,
    bool IsDangerous);
