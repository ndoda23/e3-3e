using CreditFlow.Application.Clients;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using CreditFlow.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Controllers;

/// <summary>
/// Clients: search, registration with the identity document (KYC data entered by the operator), contact details,
/// incomes and declared obligations. Reading needs <c>clients.view</c>; every change needs <c>clients.manage</c>.
/// </summary>
[HasPermission(P.Clients.View)]
public sealed class ClientsController(
    ClientService clients, CurrentAccess currentAccess, IStringLocalizer<SharedResource> localizer) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page, CancellationToken cancellationToken) =>
        View(new ClientIndexViewModel(search, await clients.SearchAsync(search, page, cancellationToken)));

    /// <summary>The list body (htmx) for the search and page.</summary>
    [HttpGet]
    public async Task<IActionResult> Rows(string? search, int page, CancellationToken cancellationToken)
    {
        if (!Request.IsHtmx())
        {
            return RedirectToAction(nameof(Index), new { search, page });
        }

        Response.PushUrl(Url.Action(nameof(Index), new { search, page = page > 1 ? page : (int?)null })!);
        return PartialView("_ClientRows", new ClientIndexViewModel(search, await clients.SearchAsync(search, page, cancellationToken)));
    }

    [HttpGet]
    [HasPermission(P.Clients.Manage)]
    public IActionResult Register() => View(new ClientForm());

    [HttpPost]
    [HasPermission(P.Clients.Manage)]
    public async Task<IActionResult> Register(ClientForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var result = await clients.RegisterAsync(form.ToCommand(), cancellationToken);
        if (result.IsSuccess)
        {
            TempData["Notice"] = localizer["Clients.Registered"].Value;
            return RedirectToAction(nameof(Details), new { id = result.Value.Value });
        }

        ModelState.AddError(result.Error!, localizer);
        return View(form);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var client = await clients.GetAsync(new ClientId(id), cancellationToken);
        return client is null
            ? NotFound()
            : View(new ClientPageViewModel(client, (await currentAccess.GetAsync())?.HasPermission(P.Clients.Manage) == true));
    }

    [HttpGet]
    [HasPermission(P.Clients.Manage)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var client = await clients.GetAsync(new ClientId(id), cancellationToken);
        return client is null ? NotFound() : View(ClientForm.From(client));
    }

    [HttpPost]
    [HasPermission(P.Clients.Manage)]
    public async Task<IActionResult> Edit(Guid id, ClientForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var result = await clients.UpdateDetailsAsync(new ClientId(id), form.ToDetails(), cancellationToken);
        if (result.IsSuccess)
        {
            TempData["Notice"] = localizer["Clients.Saved"].Value;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (result.Error!.Type == ErrorType.NotFound)
        {
            return NotFound();
        }

        ModelState.AddError(result.Error, localizer);
        return View(form);
    }

    [HttpPost]
    [HasPermission(P.Clients.Manage)]
    public async Task<IActionResult> ReplaceDocument(Guid id, IdentityDocumentForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Done(id, await clients.ReplaceDocumentAsync(
            new ClientId(id), form.Type, form.Number, form.IssuedOn ?? DateOnly.MinValue, form.ExpiresOn ?? DateOnly.MinValue,
            cancellationToken));
    }

    [HttpPost]
    [HasPermission(P.Clients.Manage)]
    public async Task<IActionResult> AddIncome(Guid id, IncomeForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Done(id, await clients.AddIncomeAsync(
            new ClientId(id), form.Type, form.Source, form.MonthlyAmount, form.Currency, cancellationToken));
    }

    [HttpPost]
    [HasPermission(P.Clients.Manage)]
    public async Task<IActionResult> RemoveIncome(Guid id, Guid incomeId, CancellationToken cancellationToken) =>
        Done(id, await clients.RemoveIncomeAsync(new ClientId(id), new ClientLineId(incomeId), cancellationToken));

    [HttpPost]
    [HasPermission(P.Clients.Manage)]
    public async Task<IActionResult> AddObligation(Guid id, ObligationForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Done(id, await clients.AddObligationAsync(
            new ClientId(id), form.Lender, form.MonthlyPayment, form.Outstanding, form.Currency, cancellationToken));
    }

    [HttpPost]
    [HasPermission(P.Clients.Manage)]
    public async Task<IActionResult> RemoveObligation(Guid id, Guid obligationId, CancellationToken cancellationToken) =>
        Done(id, await clients.RemoveObligationAsync(new ClientId(id), new ClientLineId(obligationId), cancellationToken));

    /// <summary>Back to the client page with a notice or the error; an unknown client is 404.</summary>
    private IActionResult Done(Guid id, Result result)
    {
        if (result.Error == ClientErrors.NotFound)
        {
            return NotFound();
        }

        if (result.IsSuccess)
        {
            TempData["Notice"] = localizer["Clients.Saved"].Value;
        }
        else
        {
            TempData["Error"] = ModelStateExtensions.LocalizeAll(result.Error!, localizer);
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}
