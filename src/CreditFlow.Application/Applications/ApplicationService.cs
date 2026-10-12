using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.Applications;

/// <summary>A new draft application for a registered borrower.</summary>
public sealed record CreateApplicationCommand(
    ClientId BorrowerId, LoanProductId ProductId, decimal Amount, Currency Currency, int TermMonths, decimal AnnualRate,
    ScheduleType ScheduleType);

/// <summary>Collateral with its manually entered valuation.</summary>
public sealed record AddCollateralCommand(
    CollateralType Type, string? Description, decimal MarketValue, Currency Currency, DateOnly ValuationDate);

/// <summary>A downloaded document.</summary>
public sealed record DocumentContent(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Applications for the Operator (and the read side for everyone who may view them): create, edit the draft, upload
/// documents, submit, send to scoring, record the client's answer to a counter-offer, cancel. Every command checks the
/// permission, loads the aggregate, re-checks that the user may see it (a hidden application is "not found"), and saves
/// in one transaction; the rules themselves are in <see cref="LoanApplication"/>.
/// </summary>
public sealed class ApplicationService(
    CurrentAccessProfile access,
    ILoanApplicationRepository applications,
    ILoanProductRepository products,
    IClientRepository clients,
    IFxRateRepository rates,
    IDocumentStore documents,
    IApplicationQueries queries,
    IProductQueries productQueries,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    // ----- Read side -----

    /// <summary>The active products (for new applications and list filters; empty without <c>applications.view</c>).</summary>
    public async Task<IReadOnlyList<ProductOption>> ListProductsAsync(CancellationToken cancellationToken) =>
        (await access.GetAsync(cancellationToken))?.HasPermission(Permissions.Applications.View) == true
            ? await productQueries.ListActiveAsync(cancellationToken)
            : [];

    public async Task<PagedResult<ApplicationListItem>> SearchAsync(ApplicationSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        return await queries.SearchAsync(search.Normalize(), await FilterAsync(cancellationToken), cancellationToken);
    }

    public async Task<ApplicationDetails?> GetDetailsAsync(LoanApplicationId id, CancellationToken cancellationToken) =>
        await queries.GetDetailsAsync(id, await FilterAsync(cancellationToken), cancellationToken);

    /// <summary>The document content, or <c>null</c> when the application or document is not visible.</summary>
    public async Task<DocumentContent?> DownloadAsync(
        LoanApplicationId id, ApplicationLineId documentId, CancellationToken cancellationToken)
    {
        var profile = await access.GetAsync(cancellationToken);
        if (profile is null || !profile.HasPermission(Permissions.Documents.View))
        {
            return null;
        }

        var application = await applications.GetAsync(id, cancellationToken);
        var document = application?.Documents.SingleOrDefault(d => d.Id == documentId);
        if (application is null || document is null || !ApplicationAccessFilter.For(profile).CanSee(application))
        {
            return null;
        }

        var content = await documents.GetAsync(documentId, cancellationToken);
        return content is null ? null : new DocumentContent(document.FileName, document.ContentType, content);
    }

    // ----- Draft -----

    public async Task<Result<LoanApplicationId>> CreateAsync(CreateApplicationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var profile = await access.GetAsync(cancellationToken);
        if (profile is null)
        {
            return Result.Failure<LoanApplicationId>(CommonErrors.NotAuthenticated);
        }

        if (!profile.HasPermission(Permissions.Applications.Create) || profile.BranchId is not { } branch)
        {
            return Result.Failure<LoanApplicationId>(CommonErrors.AccessDenied);
        }

        var terms = LoanTerms.Create(Money.Of(command.Amount, command.Currency), command.TermMonths, command.AnnualRate, command.ScheduleType);
        if (terms.IsFailure)
        {
            return Result.Failure<LoanApplicationId>(terms.Error!);
        }

        var product = await products.GetAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return Result.Failure<LoanApplicationId>(ProductErrors.NotFound);
        }

        if (await clients.GetAsync(command.BorrowerId, cancellationToken) is null)
        {
            return Result.Failure<LoanApplicationId>(ClientErrors.NotFound);
        }

        var created = LoanApplication.Create(product, command.BorrowerId, terms.Value, branch, profile.UserId, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<LoanApplicationId>(created.Error!);
        }

        applications.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(created.Value.Id);
    }

    public Task<Result> ChangeTermsAsync(
        LoanApplicationId id, decimal amount, Currency currency, int termMonths, decimal annualRate, ScheduleType scheduleType,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, Permissions.Applications.Create, (application, _, _) =>
        {
            var terms = LoanTerms.Create(Money.Of(amount, currency), termMonths, annualRate, scheduleType);
            return Task.FromResult(terms.IsFailure ? Result.Failure(terms.Error!) : application.ChangeTerms(terms.Value));
        }, cancellationToken);

    public Task<Result> AddPartyAsync(LoanApplicationId id, ClientId clientId, PartyRole role, CancellationToken cancellationToken) =>
        ChangeAsync(id, Permissions.Applications.Create, async (application, _, _) =>
            await clients.GetAsync(clientId, cancellationToken) is null
                ? Result.Failure(ClientErrors.NotFound)
                : application.AddParty(clientId, role), cancellationToken);

    public Task<Result> RemovePartyAsync(LoanApplicationId id, ClientId clientId, CancellationToken cancellationToken) =>
        ChangeAsync(id, Permissions.Applications.Create, (application, _, _) =>
            Task.FromResult(application.RemoveParty(clientId)), cancellationToken);

    public Task<Result> AddCollateralAsync(LoanApplicationId id, AddCollateralCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ChangeAsync(id, Permissions.Applications.Create, (application, _, now) =>
        {
            var added = application.AddCollateral(
                command.Type, command.Description, Money.Of(command.MarketValue, command.Currency), command.ValuationDate,
                BankTime.DateOf(now));
            return Task.FromResult(added.IsSuccess ? Result.Success() : Result.Failure(added.Error!));
        }, cancellationToken);
    }

    public Task<Result> RemoveCollateralAsync(LoanApplicationId id, ApplicationLineId collateralId, CancellationToken cancellationToken) =>
        ChangeAsync(id, Permissions.Applications.Create, (application, _, _) =>
            Task.FromResult(application.RemoveCollateral(collateralId)), cancellationToken);

    /// <summary>Validates the file (size, type by content), stores it and records it on the application.</summary>
    public Task<Result> UploadDocumentAsync(
        LoanApplicationId id, Domain.Products.DocumentType type, string? fileName, byte[] content, CancellationToken cancellationToken)
    {
        var upload = DocumentUploadPolicy.Validate(fileName, content);
        if (upload.IsFailure)
        {
            return Task.FromResult(Result.Failure(upload.Error!));
        }

        return ChangeAsync(id, Permissions.Documents.Upload, (application, profile, now) =>
        {
            var documentId = ApplicationLineId.New();
            var added = application.AddDocument(
                documentId, type, upload.Value.FileName, upload.Value.ContentType, content.Length, upload.Value.Sha256,
                profile.UserId, now);
            if (added.IsSuccess)
            {
                documents.Add(documentId, upload.Value.Content);
            }

            return Task.FromResult(added);
        }, cancellationToken);
    }

    public Task<Result> RemoveDocumentAsync(LoanApplicationId id, ApplicationLineId documentId, CancellationToken cancellationToken) =>
        ChangeAsync(id, Permissions.Documents.Upload, async (application, _, _) =>
        {
            var removed = application.RemoveDocument(documentId);
            if (removed.IsSuccess)
            {
                await documents.RemoveAsync(documentId, cancellationToken);
            }

            return removed;
        }, cancellationToken);

    // ----- Workflow -----

    /// <summary>Submits the draft (step 1); the pre-check then runs in the background.</summary>
    public Task<Result> SubmitAsync(LoanApplicationId id, CancellationToken cancellationToken) =>
        ChangeAsync(id, Permissions.Applications.Create, async (application, profile, now) =>
        {
            var product = await products.GetAsync(application.ProductId, cancellationToken);
            var today = BankTime.DateOf(now);
            var rate = await RateAsync(application.Terms.Amount.Currency, today, cancellationToken);
            if (product is null || rate is null)
            {
                return Result.Failure(rate is null ? FxErrors.RateMissing(application.Terms.Amount.Currency, today) : ProductErrors.NotFound);
            }

            var parties = await clients.GetManyAsync(application.Parties.Select(p => p.ClientId), cancellationToken);
            var otherOpen = await applications.HasOpenApplicationAsync(application.BorrowerId, application.ProductId, application.Id, cancellationToken);
            return application.Submit(new SubmissionContext(product, rate, parties, otherOpen, today), profile.UserId, now);
        }, cancellationToken);

    /// <summary>The checklist is complete: scoring runs in the background.</summary>
    public Task<Result> SendToScoringAsync(LoanApplicationId id, CancellationToken cancellationToken) =>
        ChangeAsync(id, Permissions.Applications.Create, (application, profile, now) =>
            Task.FromResult(application.SendToScoring(profile.UserId, now)), cancellationToken);

    /// <summary>The client accepted (scoring again) or declined (cancelled, reason required) a counter-offer.</summary>
    public Task<Result> RecordClientResponseAsync(
        LoanApplicationId id, bool accepted, string? reason, CancellationToken cancellationToken) =>
        ChangeAsync(id, Permissions.Applications.RecordClientResponse, (application, profile, now) =>
            Task.FromResult(accepted
                ? application.AcceptCounterOffer(profile.UserId, now)
                : application.DeclineCounterOffer(profile.UserId, reason, now)), cancellationToken);

    public Task<Result> CancelAsync(LoanApplicationId id, string? reason, CancellationToken cancellationToken) =>
        ChangeAsync(id, Permissions.Applications.Cancel, (application, profile, now) =>
            Task.FromResult(application.Cancel(profile.UserId, reason, now)), cancellationToken);

    private async Task<ExchangeRate?> RateAsync(Currency currency, DateOnly date, CancellationToken cancellationToken) =>
        currency == Currency.GEL
            ? ExchangeRate.Gel(date)
            : (await rates.FindEffectiveAsync(currency, date, cancellationToken))?.ToExchangeRate();

    private async Task<ApplicationAccessFilter> FilterAsync(CancellationToken cancellationToken) =>
        await access.GetAsync(cancellationToken) is { } profile
            ? ApplicationAccessFilter.For(profile)
            : new ApplicationAccessFilter(default, SeesNothing: true, null);

    private Task<Result> ChangeAsync(
        LoanApplicationId id,
        string permission,
        Func<LoanApplication, UserAccessProfile, DateTimeOffset, Task<Result>> change,
        CancellationToken cancellationToken) =>
        ApplicationChange.RunAsync(access, applications, unitOfWork, clock, id, permission, change, cancellationToken);
}

/// <summary>The one path every application command takes: permission, load, visibility, change, save, conflicts.</summary>
internal static class ApplicationChange
{
    public static async Task<Result> RunAsync(
        CurrentAccessProfile access,
        ILoanApplicationRepository applications,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        LoanApplicationId id,
        string permission,
        Func<LoanApplication, UserAccessProfile, DateTimeOffset, Task<Result>> change,
        CancellationToken cancellationToken)
    {
        var profile = await access.GetAsync(cancellationToken);
        if (profile is null)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        if (!profile.HasPermission(permission))
        {
            return Result.Failure(CommonErrors.AccessDenied);
        }

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    var application = await applications.GetAsync(id, token);
                    if (application is null || !ApplicationAccessFilter.For(profile).CanSee(application))
                    {
                        return Result.Failure(ApplicationErrors.NotFound); // never reveal that a hidden id exists
                    }

                    var result = await change(application, profile, clock.GetUtcNow());
                    if (result.IsSuccess)
                    {
                        await unitOfWork.SaveChangesAsync(token);
                    }

                    return result;
                },
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure(CommonErrors.ConcurrencyConflict);
        }
    }

    /// <summary>The same with one retry after a conflict, for commutative changes such as committee votes.</summary>
    public static async Task<Result> RunWithRetryAsync(
        CurrentAccessProfile access,
        ILoanApplicationRepository applications,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        LoanApplicationId id,
        string permission,
        Func<LoanApplication, UserAccessProfile, DateTimeOffset, Task<Result>> change,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(access, applications, unitOfWork, clock, id, permission, change, cancellationToken);
        return result.Error == CommonErrors.ConcurrencyConflict
            ? await RunAsync(access, applications, unitOfWork, clock, id, permission, change, cancellationToken)
            : result;
    }
}
