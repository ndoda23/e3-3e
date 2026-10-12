using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Clients;

public sealed record RegisterClientCommand(
    string? PersonalNumber,
    ClientDetails Details,
    IdentityDocumentType DocumentType,
    string? DocumentNumber,
    DateOnly DocumentIssuedOn,
    DateOnly DocumentExpiresOn);

public sealed record ClientListItem(ClientId Id, string PersonalNumber, string FullName, DateOnly BirthDate, KycStatus Kyc, ScreeningStatus Screening);

public sealed record IncomeView(ClientLineId Id, IncomeType Type, string Source, Money MonthlyAmount);

public sealed record ObligationView(ClientLineId Id, string Lender, Money MonthlyPayment, Money OutstandingAmount);

public sealed record ClientDetailsView(
    ClientId Id,
    string PersonalNumber,
    ClientDetails Details,
    IdentityDocument Document,
    IReadOnlyList<IncomeView> Incomes,
    IReadOnlyList<ObligationView> Obligations,
    KycStatus Kyc,
    ScreeningStatus Screening,
    DateTimeOffset? CheckedAt);

/// <summary>Read side of clients.</summary>
public interface IClientQueries
{
    Task<PagedResult<ClientListItem>> SearchAsync(string? text, int page, CancellationToken cancellationToken);

    Task<ClientDetailsView?> GetAsync(ClientId id, CancellationToken cancellationToken);

    Task<ClientId?> FindByPersonalNumberAsync(string personalNumber, CancellationToken cancellationToken);
}

/// <summary>Client registration and KYC data (the Operator); reading for everyone with <c>clients.view</c>.</summary>
public sealed class ClientService(
    CurrentAccessProfile access, IClientRepository clients, IClientQueries queries, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<PagedResult<ClientListItem>> SearchAsync(string? text, int page, CancellationToken cancellationToken) =>
        await CanAsync(Permissions.Clients.View, cancellationToken)
            ? await queries.SearchAsync(string.IsNullOrWhiteSpace(text) ? null : text.Trim(), Paging.NormalizePage(page), cancellationToken)
            : new PagedResult<ClientListItem>([], 0, 1, Paging.DefaultPageSize);

    public async Task<ClientDetailsView?> GetAsync(ClientId id, CancellationToken cancellationToken) =>
        await CanAsync(Permissions.Clients.View, cancellationToken) ? await queries.GetAsync(id, cancellationToken) : null;

    public async Task<ClientId?> FindByPersonalNumberAsync(string? personalNumber, CancellationToken cancellationToken) =>
        await CanAsync(Permissions.Clients.View, cancellationToken) && PersonalNumber.Create(personalNumber) is { IsSuccess: true } pn
            ? await queries.FindByPersonalNumberAsync(pn.Value.Value, cancellationToken)
            : null;

    public async Task<Result<ClientId>> RegisterAsync(RegisterClientCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await CanAsync(Permissions.Clients.Manage, cancellationToken))
        {
            return Result.Failure<ClientId>(CommonErrors.AccessDenied);
        }

        var today = BankTime.DateOf(clock.GetUtcNow());
        var errors = new List<Error>();
        var personalNumber = PersonalNumber.Create(command.PersonalNumber);
        if (personalNumber.IsFailure)
        {
            errors.Add(personalNumber.Error!);
        }
        else if (await clients.PersonalNumberExistsAsync(personalNumber.Value, cancellationToken))
        {
            errors.Add(ClientErrors.DuplicatePersonalNumber);
        }

        var document = IdentityDocument.Create(command.DocumentType, command.DocumentNumber, command.DocumentIssuedOn, command.DocumentExpiresOn);
        if (document.IsFailure)
        {
            errors.Add(document.Error!);
        }

        errors.AddRange(Client.Validate(command.Details, today));
        if (errors.Count > 0)
        {
            return Result.Failure<ClientId>(Error.ValidationFailed(errors));
        }

        var client = Client.Register(personalNumber.Value, command.Details, document.Value, today).Value;
        clients.Add(client);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure<ClientId>(CommonErrors.ConcurrencyConflict);
        }

        return Result.Success(client.Id);
    }

    public Task<Result> UpdateDetailsAsync(ClientId id, ClientDetails details, CancellationToken cancellationToken) =>
        ChangeAsync(id, client => client.UpdateDetails(details, BankTime.DateOf(clock.GetUtcNow())), cancellationToken);

    public Task<Result> ReplaceDocumentAsync(
        ClientId id, IdentityDocumentType type, string? number, DateOnly issuedOn, DateOnly expiresOn, CancellationToken cancellationToken) =>
        ChangeAsync(id, client =>
        {
            var document = IdentityDocument.Create(type, number, issuedOn, expiresOn);
            if (document.IsFailure)
            {
                return Result.Failure(document.Error!);
            }

            client.ReplaceIdentityDocument(document.Value);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> AddIncomeAsync(ClientId id, IncomeType type, string? source, decimal amount, Currency currency, CancellationToken cancellationToken) =>
        ChangeAsync(id, client => client.AddIncome(type, source, Money.Of(amount, currency)) is { IsFailure: true } r
            ? Result.Failure(r.Error!)
            : Result.Success(), cancellationToken);

    public Task<Result> RemoveIncomeAsync(ClientId id, ClientLineId incomeId, CancellationToken cancellationToken) =>
        ChangeAsync(id, client => client.RemoveIncome(incomeId), cancellationToken);

    public Task<Result> AddObligationAsync(
        ClientId id, string? lender, decimal monthlyPayment, decimal outstanding, Currency currency, CancellationToken cancellationToken) =>
        ChangeAsync(id, client => client.AddObligation(lender, Money.Of(monthlyPayment, currency), Money.Of(outstanding, currency)) is { IsFailure: true } r
            ? Result.Failure(r.Error!)
            : Result.Success(), cancellationToken);

    public Task<Result> RemoveObligationAsync(ClientId id, ClientLineId obligationId, CancellationToken cancellationToken) =>
        ChangeAsync(id, client => client.RemoveObligation(obligationId), cancellationToken);

    private async Task<Result> ChangeAsync(ClientId id, Func<Client, Result> change, CancellationToken cancellationToken)
    {
        if (!await CanAsync(Permissions.Clients.Manage, cancellationToken))
        {
            return Result.Failure(CommonErrors.AccessDenied);
        }

        var client = await clients.GetAsync(id, cancellationToken);
        if (client is null)
        {
            return Result.Failure(ClientErrors.NotFound);
        }

        var result = change(client);
        if (result.IsFailure)
        {
            return result;
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure(CommonErrors.ConcurrencyConflict);
        }
    }

    private async Task<bool> CanAsync(string permission, CancellationToken cancellationToken) =>
        (await access.GetAsync(cancellationToken))?.HasPermission(permission) == true;
}
