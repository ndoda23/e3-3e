using CreditFlow.Domain.Common;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;

namespace CreditFlow.Web.Infrastructure;

public static class ModelStateExtensions
{
    /// <summary>
    /// Adds a domain error (and its field details) to the model state. Texts are localized by code
    /// (<c>Error.{Code}</c>) with the English description as fallback. <paramref name="fieldMap"/> maps an error target to
    /// the form field name when they differ.
    /// </summary>
    public static void AddError(
        this ModelStateDictionary modelState,
        Error error,
        IStringLocalizer localizer,
        IReadOnlyDictionary<string, string>? fieldMap = null)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(localizer);

        var errors = error.Details.Count > 0 ? error.Details : [error];
        foreach (var item in errors)
        {
            var field = item.Target is null ? string.Empty : fieldMap?.GetValueOrDefault(item.Target) ?? item.Target;
            modelState.AddModelError(field, Localize(item, localizer));
        }
    }

    /// <summary>One message for an error and all its details (for a notice after a redirect).</summary>
    public static string LocalizeAll(Error error, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(error);
        return string.Join(" ", (error.Details.Count > 0 ? error.Details : [error]).Select(e => Localize(e, localizer)));
    }

    public static string Localize(Error error, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(localizer);

        var text = localizer[$"Error.{error.Code}"];
        return text.ResourceNotFound ? error.Description : text.Value;
    }
}
