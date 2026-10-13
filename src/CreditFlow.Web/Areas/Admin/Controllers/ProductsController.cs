using CreditFlow.Application.Administration;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using CreditFlow.Web.Areas.Admin.Models;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Areas.Admin.Controllers;

/// <summary>
/// Admin › Products. Terms are versioned: saving terms publishes the next version, which applies to applications
/// submitted from then on (submitted ones keep their snapshot). The code is fixed; the name and the active flag change.
/// </summary>
[HasPermission(P.Admin.ProductsManage)]
public sealed class ProductsController(ProductAdministrationService products, IStringLocalizer<SharedResource> localizer)
    : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await products.ListAsync(cancellationToken));

    [HttpGet]
    public IActionResult Create() => View(new ProductCreateForm());

    [HttpPost]
    public async Task<IActionResult> Create(ProductCreateForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var created = await products.CreateAsync(
            new CreateProductCommand(form.Code, form.Name, form.Type, form.Terms.ToTerms()), cancellationToken);
        if (created.IsSuccess)
        {
            TempData["Notice"] = localizer["Products.Created"].Value;
            return RedirectToAction(nameof(Details), new { id = created.Value.Value });
        }

        ModelState.AddError(created.Error!, localizer, TermFields);
        return View(form);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var product = await products.GetAsync(new LoanProductId(id), cancellationToken);
        return product is null
            ? NotFound()
            : View(new ProductPageViewModel(product, ProductTermsForm.From(product.Versions[0].Terms)));
    }

    /// <summary>Publishes the form's terms as the next version.</summary>
    [HttpPost]
    public async Task<IActionResult> PublishVersion(Guid id, ProductTermsForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var productId = new LoanProductId(id);
        var published = await products.PublishVersionAsync(productId, form.ToTerms(), cancellationToken);
        if (published.IsSuccess)
        {
            TempData["Notice"] = localizer["Products.VersionPublished"].Value;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (published.Error == ProductErrors.NotFound)
        {
            return NotFound();
        }

        var product = await products.GetAsync(productId, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        // Errors next to the fields of the version form.
        ModelState.AddError(published.Error!, localizer);
        return View(nameof(Details), new ProductPageViewModel(product, form));
    }

    [HttpPost]
    public async Task<IActionResult> Rename(Guid id, string? name, CancellationToken cancellationToken) =>
        Done(id, await products.RenameAsync(new LoanProductId(id), name, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        Done(id, await products.SetActiveAsync(new LoanProductId(id), active: true, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        Done(id, await products.SetActiveAsync(new LoanProductId(id), active: false, cancellationToken));

    /// <summary>Term errors carry the bare field name; on the create form the terms are nested.</summary>
    private static readonly IReadOnlyDictionary<string, string> TermFields = typeof(ProductTermsForm).GetProperties()
        .ToDictionary(p => p.Name, p => $"{nameof(ProductCreateForm.Terms)}.{p.Name}", StringComparer.Ordinal);

    private IActionResult Done(Guid id, Result result)
    {
        if (result.Error == ProductErrors.NotFound)
        {
            return NotFound();
        }

        if (result.IsSuccess)
        {
            TempData["Notice"] = localizer["Products.Saved"].Value;
        }
        else
        {
            TempData["Error"] = ModelStateExtensions.LocalizeAll(result.Error!, localizer);
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}
