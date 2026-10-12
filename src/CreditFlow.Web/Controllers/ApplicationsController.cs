using CreditFlow.Application.Applications;
using CreditFlow.Application.Clients;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using CreditFlow.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Controllers;

/// <summary>
/// Loan applications: the list (filtered, sorted and paged in SQL within the viewer's scope), a new application, and
/// the application page with every action of the process the viewer may take. Each action is protected by its
/// permission here and again in the services, which load the application through the viewer's access filter: an
/// application outside the scope is 404 whatever the id.
/// </summary>
[HasPermission(P.Applications.View)]
public sealed class ApplicationsController(
    ApplicationService applications,
    DecisionService decisions,
    ClientService clients,
    CurrentAccess currentAccess,
    IStringLocalizer<SharedResource> localizer) : Controller
{
    // ----- Read -----

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ApplicationListQuery query, CancellationToken cancellationToken) =>
        View(await BuildIndexAsync(query, cancellationToken));

    /// <summary>The list body (htmx) for the current filters.</summary>
    [HttpGet]
    public async Task<IActionResult> Rows([FromQuery] ApplicationListQuery query, CancellationToken cancellationToken)
    {
        if (!Request.IsHtmx())
        {
            return RedirectToAction(nameof(Index), query.RouteValues());
        }

        var model = await BuildIndexAsync(query, cancellationToken);
        Response.PushUrl(Url.Action(nameof(Index), model.Query.RouteValues())!);
        return PartialView("_ApplicationRows", model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var profile = await currentAccess.GetAsync();
        var application = await applications.GetDetailsAsync(new LoanApplicationId(id), cancellationToken);
        if (profile is null || application is null)
        {
            return NotFound();
        }

        var canOverride = profile.HasPermission(P.Scoring.Override);
        return View(new ApplicationPageViewModel(application, ApplicationActions.For(profile, application), canOverride));
    }

    [HttpGet]
    [HasPermission(P.Documents.View)]
    public async Task<IActionResult> Document(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await applications.DownloadAsync(
            new LoanApplicationId(id), new ApplicationLineId(documentId), cancellationToken);
        return document is null ? NotFound() : File(document.Content, document.ContentType, document.FileName);
    }

    // ----- New application -----

    [HttpGet("/Applications/New")]
    [HasPermission(P.Applications.Create)]
    public async Task<IActionResult> New(string? personalNumber, CancellationToken cancellationToken) =>
        View(new NewApplicationViewModel(
            new NewApplicationForm { BorrowerPersonalNumber = personalNumber },
            await applications.ListProductsAsync(cancellationToken)));

    [HttpPost("/Applications/New")]
    [HasPermission(P.Applications.Create)]
    public async Task<IActionResult> New(NewApplicationForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var borrower = await clients.FindByPersonalNumberAsync(form.BorrowerPersonalNumber, cancellationToken);
        if (borrower is null)
        {
            ModelState.AddModelError(nameof(form.BorrowerPersonalNumber), localizer["Applications.BorrowerNotFound"].Value);
        }
        else if (form.ProductId is not { } product)
        {
            ModelState.AddModelError(nameof(form.ProductId), localizer["Applications.ChooseProduct"].Value);
        }
        else
        {
            var created = await applications.CreateAsync(
                new CreateApplicationCommand(
                    borrower.Value, new LoanProductId(product), form.Amount, form.Currency, form.TermMonths, form.AnnualRate,
                    form.ScheduleType),
                cancellationToken);
            if (created.IsSuccess)
            {
                TempData["Notice"] = localizer["Applications.Created"].Value;
                return RedirectToAction(nameof(Details), new { id = created.Value.Value });
            }

            ModelState.AddError(created.Error!, localizer);
        }

        return View(new NewApplicationViewModel(form, await applications.ListProductsAsync(cancellationToken)));
    }

    // ----- Draft -----

    [HttpPost]
    [HasPermission(P.Applications.Create)]
    public async Task<IActionResult> ChangeTerms(Guid id, TermsForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Done(id, await applications.ChangeTermsAsync(
            new LoanApplicationId(id), form.Amount, form.Currency, form.TermMonths, form.AnnualRate, form.ScheduleType,
            cancellationToken));
    }

    [HttpPost]
    [HasPermission(P.Applications.Create)]
    public async Task<IActionResult> AddParty(Guid id, PartyForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var client = await clients.FindByPersonalNumberAsync(form.PersonalNumber, cancellationToken);
        return client is null
            ? Done(id, Result.Failure(ClientErrors.NotFound))
            : Done(id, await applications.AddPartyAsync(new LoanApplicationId(id), client.Value, form.Role, cancellationToken));
    }

    [HttpPost]
    [HasPermission(P.Applications.Create)]
    public async Task<IActionResult> RemoveParty(Guid id, Guid clientId, CancellationToken cancellationToken) =>
        Done(id, await applications.RemovePartyAsync(new LoanApplicationId(id), new ClientId(clientId), cancellationToken));

    [HttpPost]
    [HasPermission(P.Applications.Create)]
    public async Task<IActionResult> AddCollateral(Guid id, CollateralForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Done(id, await applications.AddCollateralAsync(new LoanApplicationId(id), form.ToCommand(), cancellationToken));
    }

    [HttpPost]
    [HasPermission(P.Applications.Create)]
    public async Task<IActionResult> RemoveCollateral(Guid id, Guid collateralId, CancellationToken cancellationToken) =>
        Done(id, await applications.RemoveCollateralAsync(
            new LoanApplicationId(id), new ApplicationLineId(collateralId), cancellationToken));

    /// <summary>Uploads a document. The size is checked before the file is read; the type is checked by content.</summary>
    [HttpPost]
    [HasPermission(P.Documents.Upload)]
    [RequestFormLimits(MultipartBodyLengthLimit = DocumentUploadPolicy.MaxBytes + (64 * 1024))]
    [RequestSizeLimit(DocumentUploadPolicy.MaxBytes + (64 * 1024))]
    public async Task<IActionResult> Upload(Guid id, DocumentType type, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Done(id, Result.Failure(DocumentUploadPolicy.Empty));
        }

        if (file.Length > DocumentUploadPolicy.MaxBytes)
        {
            return Done(id, Result.Failure(DocumentUploadPolicy.TooLarge));
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        return Done(id, await applications.UploadDocumentAsync(
            new LoanApplicationId(id), type, file.FileName, buffer.ToArray(), cancellationToken));
    }

    [HttpPost]
    [HasPermission(P.Documents.Upload)]
    public async Task<IActionResult> RemoveDocument(Guid id, Guid documentId, CancellationToken cancellationToken) =>
        Done(id, await applications.RemoveDocumentAsync(
            new LoanApplicationId(id), new ApplicationLineId(documentId), cancellationToken));

    // ----- Workflow -----

    [HttpPost]
    [HasPermission(P.Applications.Create)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        Done(id, await applications.SubmitAsync(new LoanApplicationId(id), cancellationToken), "Applications.Submitted");

    [HttpPost]
    [HasPermission(P.Applications.Create)]
    public async Task<IActionResult> SendToScoring(Guid id, CancellationToken cancellationToken) =>
        Done(id, await applications.SendToScoringAsync(new LoanApplicationId(id), cancellationToken), "Applications.SentToScoring");

    [HttpPost]
    [HasPermission(P.Applications.RecordClientResponse)]
    public async Task<IActionResult> ClientResponse(Guid id, bool accepted, string? reason, CancellationToken cancellationToken) =>
        Done(id, await applications.RecordClientResponseAsync(new LoanApplicationId(id), accepted, reason, cancellationToken));

    [HttpPost]
    [HasPermission(P.Applications.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, string? reason, CancellationToken cancellationToken) =>
        Done(id, await applications.CancelAsync(new LoanApplicationId(id), reason, cancellationToken), "Applications.Cancelled");

    // ----- Decisions -----

    [HttpPost]
    [HasPermission(P.Compliance.Review)]
    public async Task<IActionResult> Compliance(Guid id, bool clear, string? reason, CancellationToken cancellationToken) =>
        Done(id, await decisions.DecideComplianceAsync(new LoanApplicationId(id), clear, reason, cancellationToken), "Applications.Decided");

    [HttpPost]
    [HasPermission(P.Underwriting.Decide)]
    public async Task<IActionResult> Approve(Guid id, ApproveForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Done(id, await decisions.ApproveAsync(
            new LoanApplicationId(id), form.Conditions(), form.OverrideReason, cancellationToken), "Applications.Decided");
    }

    [HttpPost]
    [HasPermission(P.Underwriting.Decide)]
    public async Task<IActionResult> Reject(Guid id, string? reason, CancellationToken cancellationToken) =>
        Done(id, await decisions.RejectAsync(new LoanApplicationId(id), reason, cancellationToken), "Applications.Decided");

    [HttpPost]
    [HasPermission(P.Underwriting.CounterOffer)]
    public async Task<IActionResult> CounterOffer(Guid id, TermsForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Done(id, await decisions.CounterOfferAsync(
            new LoanApplicationId(id), form.Amount, form.TermMonths, form.AnnualRate, form.Reason, cancellationToken),
            "Applications.Decided");
    }

    [HttpPost]
    [HasPermission(P.Committee.Vote)]
    public async Task<IActionResult> Vote(Guid id, bool approve, string? reason, CancellationToken cancellationToken) =>
        Done(id, await decisions.VoteAsync(new LoanApplicationId(id), approve, reason, cancellationToken), "Applications.Voted");

    /// <summary>
    /// Back to the application page with a notice or the error. An application the viewer may not see is 404 —
    /// the same answer as for an id that does not exist.
    /// </summary>
    private IActionResult Done(Guid id, Result result, string noticeKey = "Applications.Saved")
    {
        if (result.Error == ApplicationErrors.NotFound)
        {
            return NotFound();
        }

        if (result.IsSuccess)
        {
            TempData["Notice"] = localizer[noticeKey].Value;
        }
        else
        {
            TempData["Error"] = ModelStateExtensions.LocalizeAll(result.Error!, localizer);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<ApplicationIndexViewModel> BuildIndexAsync(ApplicationListQuery query, CancellationToken cancellationToken)
    {
        var page = await applications.SearchAsync(query.ToSearch(), cancellationToken);
        if (page.Page > page.TotalPages)
        {
            query.Page = page.TotalPages;
            page = await applications.SearchAsync(query.ToSearch(), cancellationToken);
        }

        return new ApplicationIndexViewModel(query, page, await applications.ListProductsAsync(cancellationToken));
    }
}
