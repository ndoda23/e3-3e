using CreditFlow.Application.Applications;
using CreditFlow.Application.Clients;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Queries;

internal sealed class ClientQueries(CreditFlowDbContext db) : IClientQueries
{
    public async Task<PagedResult<ClientListItem>> SearchAsync(string? text, int page, CancellationToken cancellationToken)
    {
        var clients = db.Clients.AsNoTracking();
        if (text is not null)
        {
            clients = PersonalNumber.Create(text) is { IsSuccess: true } number
                ? clients.Where(c => c.PersonalNumber == number.Value)
                : clients.Where(c => (c.FirstName + " " + c.LastName).Contains(text));
        }

        var total = await clients.CountAsync(cancellationToken);
        var rows = await clients
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Skip((page - 1) * Paging.DefaultPageSize).Take(Paging.DefaultPageSize)
            .Select(c => new { c.Id, c.PersonalNumber, c.FirstName, c.LastName, c.BirthDate, c.KycStatus, c.ScreeningStatus })
            .ToListAsync(cancellationToken);

        return new PagedResult<ClientListItem>(
            rows.Select(c => new ClientListItem(c.Id, c.PersonalNumber.Value, $"{c.FirstName} {c.LastName}", c.BirthDate, c.KycStatus, c.ScreeningStatus)).ToList(),
            total, page, Paging.DefaultPageSize);
    }

    public async Task<ClientDetailsView?> GetAsync(ClientId id, CancellationToken cancellationToken)
    {
        var client = await db.Clients.AsNoTracking()
            .Include(c => c.Incomes).Include(c => c.Obligations).AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        return client is null
            ? null
            : new ClientDetailsView(
                client.Id,
                client.PersonalNumber.Value,
                new ClientDetails(client.FirstName, client.LastName, client.BirthDate, client.Phone, client.Email, client.Address),
                client.IdentityDocument,
                client.Incomes.Select(i => new IncomeView(i.Id, i.Type, i.Source, i.MonthlyAmount)).ToList(),
                client.Obligations.Select(o => new ObligationView(o.Id, o.Lender, o.MonthlyPayment, o.OutstandingAmount)).ToList(),
                client.KycStatus,
                client.ScreeningStatus,
                client.CheckedAt);
    }

    public async Task<ClientId?> FindByPersonalNumberAsync(string personalNumber, CancellationToken cancellationToken)
    {
        var number = PersonalNumber.Create(personalNumber).Value;
        var ids = await db.Clients.Where(c => c.PersonalNumber == number).Select(c => c.Id).Take(1).ToListAsync(cancellationToken);
        return ids.Count == 0 ? null : ids[0];
    }
}

internal sealed class ProductQueries(CreditFlowDbContext db) : IProductQueries
{
    public async Task<IReadOnlyList<ProductOption>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var products = await db.LoanProducts.AsNoTracking()
            .Where(p => p.IsActive)
            .Include(p => p.Versions)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
        return products.Select(p => new ProductOption(p.Id, p.Code, p.Name, p.Type, p.CurrentVersion.Terms)).ToList();
    }
}

/// <summary>
/// Exposure from applications: approved and not yet disbursed, disbursed and active ones count with their GEL amount
/// for every party (borrower, co-borrower and guarantor alike — a guarantee covers the full amount).
/// </summary>
internal sealed class ExposureQueries(CreditFlowDbContext db) : IExposureQueries
{
    private static readonly ApplicationStatus[] Exposed =
    [
        ApplicationStatus.Approved, ApplicationStatus.ConditionallyApproved, ApplicationStatus.ContractSigned,
        ApplicationStatus.ConditionsMet, ApplicationStatus.Disbursed, ApplicationStatus.Active,
    ];

    public async Task<IReadOnlyDictionary<ClientId, decimal>> ExposureGelAsync(
        IReadOnlyCollection<ClientId> clients, LoanApplicationId except, CancellationToken cancellationToken) =>
        await db.LoanApplications.AsNoTracking()
            .Where(a => a.Id != except && Exposed.Contains(a.Status))
            .SelectMany(a => a.Parties.Select(p => new { p.ClientId, Amount = a.AmountGel ?? 0m }))
            .Where(x => clients.Contains(x.ClientId))
            .GroupBy(x => x.ClientId)
            .Select(g => new { ClientId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.ClientId, x => x.Total, cancellationToken);

    /// <summary>Installments of loans at this bank; loans are booked from P11 on, until then there are none.</summary>
    public Task<IReadOnlyDictionary<ClientId, decimal>> MonthlyInstallmentsGelAsync(
        IReadOnlyCollection<ClientId> clients, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<ClientId, decimal>>(new Dictionary<ClientId, decimal>());
}

/// <summary>Document content in its own table, so that application queries never load it.</summary>
public sealed class DocumentContentRecord
{
    public ApplicationLineId Id { get; set; }

    public byte[] Content { get; set; } = [];
}

internal sealed class DocumentStore(CreditFlowDbContext db) : IDocumentStore
{
    public void Add(ApplicationLineId id, byte[] content) =>
        db.Set<DocumentContentRecord>().Add(new DocumentContentRecord { Id = id, Content = content });

    public async Task<byte[]?> GetAsync(ApplicationLineId id, CancellationToken cancellationToken) =>
        await db.Set<DocumentContentRecord>().AsNoTracking()
            .Where(d => d.Id == id).Select(d => d.Content).FirstOrDefaultAsync(cancellationToken);

    public Task RemoveAsync(ApplicationLineId id, CancellationToken cancellationToken) =>
        db.Set<DocumentContentRecord>().Where(d => d.Id == id).ExecuteDeleteAsync(cancellationToken);
}
