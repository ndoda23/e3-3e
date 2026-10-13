using CreditFlow.Application.Administration;
using CreditFlow.Domain.Common;
using CreditFlow.Web.Areas.Admin.Models;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Areas.Admin.Controllers;

/// <summary>Admin › Approval thresholds: the underwriter and senior limits (GEL) and the committee's deciding votes.</summary>
[HasPermission(P.Admin.ApprovalThresholdsManage)]
public sealed class ApprovalThresholdsController(
    ApprovalThresholdsAdministrationService thresholds, IStringLocalizer<SharedResource> localizer) : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await thresholds.GetAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Index(
        decimal underwriterMaxGel, decimal seniorUnderwriterMaxGel, int requiredVotes, CancellationToken cancellationToken)
    {
        var changed = await thresholds.ChangeAsync(underwriterMaxGel, seniorUnderwriterMaxGel, requiredVotes, cancellationToken);
        TempData[changed.IsSuccess ? "Notice" : "Error"] = changed.IsSuccess
            ? localizer["Thresholds.Saved"].Value
            : ModelStateExtensions.LocalizeAll(changed.Error!, localizer);
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Admin › FX rates: list by currency, set a rate for a day, delete, import a CSV file (all or nothing).</summary>
[HasPermission(P.Admin.FxRatesManage)]
public sealed class FxRatesController(FxRateAdministrationService rates, IStringLocalizer<SharedResource> localizer) : AdminController
{
    /// <summary>Import files are small text files; anything larger is refused before it is read.</summary>
    private const int MaxImportBytes = 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Index(Currency? currency, int page, CancellationToken cancellationToken)
    {
        var model = new FxRatesViewModel(currency, await rates.ListAsync(currency, page, cancellationToken));
        return Request.IsHtmx() ? PartialView("_FxRows", model) : View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Set(Currency currency, DateOnly? effectiveDate, decimal gelPerUnit, CancellationToken cancellationToken) =>
        Done(effectiveDate is { } date
            ? await rates.SetAsync(currency, date, gelPerUnit, cancellationToken)
            : Result.Failure(AdministrationErrors.DateRequired), "FxRates.Saved");

    [HttpPost]
    public async Task<IActionResult> Delete(Currency currency, DateOnly effectiveDate, CancellationToken cancellationToken) =>
        Done(await rates.DeleteAsync(currency, effectiveDate, cancellationToken), "FxRates.Deleted");

    [HttpPost]
    [RequestSizeLimit(MaxImportBytes + (16 * 1024))]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportBytes + (16 * 1024))]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Done(Result.Failure(FxRateCsv.Empty), "FxRates.Imported");
        }

        if (file.Length > MaxImportBytes)
        {
            return Done(Result.Failure(FxRateCsv.TooLong), "FxRates.Imported");
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var imported = await rates.ImportAsync(await reader.ReadToEndAsync(cancellationToken), cancellationToken);
        if (imported.IsSuccess)
        {
            TempData["Notice"] = localizer["FxRates.Imported", imported.Value].Value;
            return RedirectToAction(nameof(Index));
        }

        return Done(Result.Failure(imported.Error!), "FxRates.Imported");
    }

    private RedirectToActionResult Done(Result result, string noticeKey)
    {
        TempData[result.IsSuccess ? "Notice" : "Error"] = result.IsSuccess
            ? localizer[noticeKey].Value
            : ModelStateExtensions.LocalizeAll(result.Error!, localizer);
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Admin › Holidays: the public holidays of a year (they do not count as working days).</summary>
[HasPermission(P.Admin.HolidaysManage)]
public sealed class HolidaysController(
    HolidayAdministrationService holidays, TimeProvider clock, IStringLocalizer<SharedResource> localizer) : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(int? year, CancellationToken cancellationToken)
    {
        var shown = year is >= 2000 and <= 2100 ? year.Value : BankTime.DateOf(clock.GetUtcNow()).Year;
        return View(new HolidaysViewModel(shown, await holidays.ListAsync(shown, cancellationToken)));
    }

    [HttpPost]
    public async Task<IActionResult> Add(DateOnly? date, string? name, CancellationToken cancellationToken) =>
        Done(date, date is { } day
            ? await holidays.AddAsync(day, name, cancellationToken)
            : Result.Failure(AdministrationErrors.DateRequired), "Holidays.Added");

    [HttpPost]
    public async Task<IActionResult> Rename(DateOnly date, string? name, CancellationToken cancellationToken) =>
        Done(date, await holidays.RenameAsync(date, name, cancellationToken), "Holidays.Saved");

    [HttpPost]
    public async Task<IActionResult> Remove(DateOnly date, CancellationToken cancellationToken) =>
        Done(date, await holidays.RemoveAsync(date, cancellationToken), "Holidays.Removed");

    private RedirectToActionResult Done(DateOnly? date, Result result, string noticeKey)
    {
        TempData[result.IsSuccess ? "Notice" : "Error"] = result.IsSuccess
            ? localizer[noticeKey].Value
            : ModelStateExtensions.LocalizeAll(result.Error!, localizer);
        return RedirectToAction(nameof(Index), new { year = date?.Year });
    }
}
