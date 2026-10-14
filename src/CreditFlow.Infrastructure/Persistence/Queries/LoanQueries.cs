using CreditFlow.Application.Applications;
using CreditFlow.Application.Common;
using CreditFlow.Application.Disbursements;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Loans;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Queries;

/// <summary>
/// Exposure of clients at the bank, in GEL at the rate fixed when each application was scored. Every party counts the
/// full amount (a guarantee covers the whole loan). Once a loan is booked it counts with its outstanding principal.
/// </summary>
internal sealed class ExposureQueries(CreditFlowDbContext db) : IExposureQueries
{
    private static readonly ApplicationStatus[] Approved =
    [
        ApplicationStatus.Approved, ApplicationStatus.ConditionallyApproved, ApplicationStatus.ContractSigned,
        ApplicationStatus.ConditionsMet,
    ];

    private static readonly ApplicationStatus[] PaidOut = [ApplicationStatus.Disbursed, ApplicationStatus.Active];

    public async Task<IReadOnlyDictionary<ClientId, decimal>> ExposureGelAsync(
        IReadOnlyCollection<ClientId> clients, LoanApplicationId except, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(clients, except, [.. Approved, .. PaidOut], cancellationToken);
        return await TotalsAsync(rows, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<ClientId, decimal>> CommittedExposureGelAsync(
        IReadOnlyCollection<ClientId> clients, LoanApplicationId except, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(clients, except, [ApplicationStatus.ConditionsMet, .. PaidOut], cancellationToken);
        var ids = rows.Select(r => r.Id).Distinct().ToList();
        var paid = await db.Disbursements.AsNoTracking()
            .Where(d => ids.Contains(d.ApplicationId) && d.Status != DisbursementStatus.Failed)
            .Select(d => d.ApplicationId)
            .ToListAsync(cancellationToken);
        return await TotalsAsync(rows.Where(r => paid.Contains(r.Id)).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyDictionary<ClientId, decimal>> MonthlyInstallmentsGelAsync(
        IReadOnlyCollection<ClientId> clients, CancellationToken cancellationToken)
    {
        var rows = (await RowsAsync(clients, default, [ApplicationStatus.Active], cancellationToken))
            .Where(r => r.Role is PartyRole.Borrower or PartyRole.CoBorrower)
            .ToList();
        var loans = await LoansAsync(rows, cancellationToken);
        return rows
            .Where(r => loans.TryGetValue(r.Id, out var loan) && loan.Status == LoanStatus.Active)
            .GroupBy(r => r.ClientId)
            .ToDictionary(g => g.Key, g => g.Sum(r => Money.Round(loans[r.Id].NextInstallment * r.GelPerUnit)));
    }

    private async Task<IReadOnlyDictionary<ClientId, decimal>> TotalsAsync(List<Row> rows, CancellationToken cancellationToken)
    {
        var loans = await LoansAsync(rows, cancellationToken);
        return rows
            .GroupBy(r => r.ClientId)
            .ToDictionary(g => g.Key, g => g.Sum(r => loans.TryGetValue(r.Id, out var loan)
                ? loan.Status == LoanStatus.Active ? Money.Round(loan.Outstanding * r.GelPerUnit) : 0m
                : r.AmountGel));
    }

    private Task<List<Row>> RowsAsync(
        IReadOnlyCollection<ClientId> clients, LoanApplicationId except, ApplicationStatus[] statuses,
        CancellationToken cancellationToken) =>
        db.LoanApplications.AsNoTracking()
            .Where(a => a.Id != except && statuses.Contains(a.Status))
            .SelectMany(a => a.Parties
                .Where(p => clients.Contains(p.ClientId))
                .Select(p => new Row(a.Id, p.ClientId, p.Role, a.AmountGel ?? 0m, a.Terms.Amount.Amount)))
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<LoanApplicationId, LoanFigures>> LoansAsync(List<Row> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(r => r.Id).Distinct().ToList();
        return await db.Loans.AsNoTracking()
            .Where(l => ids.Contains(l.ApplicationId))
            .Select(l => new LoanFigures(
                l.ApplicationId,
                l.Status,
                l.OutstandingPrincipal.Amount,
                l.Installments.Where(i => !i.IsPaid).OrderBy(i => i.Number)
                    .Select(i => i.Principal.Amount + i.Interest.Amount + i.Fee.Amount).FirstOrDefault()))
            .ToDictionaryAsync(l => l.ApplicationId, cancellationToken);
    }

    private sealed record Row(LoanApplicationId Id, ClientId ClientId, PartyRole Role, decimal AmountGel, decimal Amount)
    {
        /// <summary>GEL per unit of the loan currency, as fixed at scoring.</summary>
        public decimal GelPerUnit => Amount == 0m ? 0m : AmountGel / Amount;
    }

    private sealed record LoanFigures(LoanApplicationId ApplicationId, LoanStatus Status, decimal Outstanding, decimal NextInstallment);
}

/// <summary>The loan page; a loan is visible exactly when its application is.</summary>
internal sealed class LoanQueries(CreditFlowDbContext db) : ILoanQueries
{
    /// <summary>The access filter, the search and the paging all run in SQL (newest disbursement first).</summary>
    public async Task<PagedResult<LoanListItem>> SearchAsync(
        LoanSearch search, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(access);
        if (access.SeesNothing)
        {
            return new PagedResult<LoanListItem>([], 0, search.Page, Paging.DefaultPageSize);
        }

        var applications = db.LoanApplications.AsNoTracking();
        if (access.Branch is { } branch)
        {
            applications = applications.Where(a => a.BranchId == branch);
        }

        var rows =
            from l in db.Loans.AsNoTracking()
            join a in applications on l.ApplicationId equals a.Id
            join c in db.Clients on l.BorrowerId equals c.Id
            join p in db.LoanProducts on l.ProductId equals p.Id
            select new { Loan = l, c.FirstName, c.LastName, c.PersonalNumber, ProductName = p.Name };

        if (search.Status is { } status)
        {
            rows = rows.Where(r => r.Loan.Status == status);
        }

        if (search.Text is { } text)
        {
            rows = PersonalNumber.Create(text) is { IsSuccess: true } number
                ? rows.Where(r => r.PersonalNumber == number.Value)
                : rows.Where(r => r.Loan.ContractNumber == text || (r.FirstName + " " + r.LastName).Contains(text));
        }

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(r => r.Loan.DisbursedOn).ThenBy(r => r.Loan.ContractNumber).ThenBy(r => r.Loan.Id)
            .Skip((search.Page - 1) * Paging.DefaultPageSize).Take(Paging.DefaultPageSize)
            .Select(r => new
            {
                r.Loan.Id,
                r.Loan.ContractNumber,
                Borrower = r.FirstName + " " + r.LastName,
                r.PersonalNumber,
                r.ProductName,
                r.Loan.Principal,
                r.Loan.OutstandingPrincipal,
                r.Loan.Status,
                r.Loan.DisbursedOn,
                NextDueDate = r.Loan.Installments.Where(i => !i.IsPaid).OrderBy(i => i.Number).Select(i => (DateOnly?)i.DueDate).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<LoanListItem>(
            page.Select(r => new LoanListItem(
                r.Id, r.ContractNumber, r.Borrower, r.PersonalNumber.Value, r.ProductName, r.Principal, r.OutstandingPrincipal,
                r.Status, r.DisbursedOn, r.NextDueDate)).ToList(),
            total, search.Page, Paging.DefaultPageSize);
    }

    public async Task<LoanDetails?> GetAsync(LoanId id, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        var loan = await db.Loans.AsNoTracking()
            .Include(l => l.Installments.OrderBy(i => i.Number))
            .Include(l => l.Repayments.OrderBy(r => r.RecordedAt))
            .AsSplitQuery()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (loan is null || !await IsVisibleAsync(loan.ApplicationId, access, cancellationToken))
        {
            return null;
        }

        var borrower = await db.Clients.Where(c => c.Id == loan.BorrowerId)
            .Select(c => c.FirstName + " " + c.LastName).SingleAsync(cancellationToken);
        var product = await db.LoanProducts.Where(p => p.Id == loan.ProductId).Select(p => p.Name).SingleAsync(cancellationToken);
        var applicationNumber = await db.LoanApplications.Where(a => a.Id == loan.ApplicationId).Select(a => a.Number).SingleAsync(cancellationToken);
        var userIds = loan.Repayments.Select(r => r.RecordedBy.Value).Distinct().ToList();
        var names = await db.UserDirectory.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return new LoanDetails
        {
            Id = loan.Id,
            ApplicationId = loan.ApplicationId,
            ApplicationReference = LoanApplication.FormatReference(applicationNumber),
            ContractNumber = loan.ContractNumber,
            BorrowerName = borrower,
            ProductName = product,
            Principal = loan.Principal,
            AnnualRate = loan.AnnualRate,
            EffectiveAnnualRate = loan.EffectiveAnnualRate,
            ScheduleType = loan.ScheduleType,
            DisbursedOn = loan.DisbursedOn,
            Status = loan.Status,
            OutstandingPrincipal = loan.OutstandingPrincipal,
            EarlyRepaymentPenaltyPercent = loan.EarlyRepaymentPenaltyPercent,
            Installments = loan.Installments
                .Select(i => new InstallmentView(i.Number, i.DueDate, i.Principal, i.Interest, i.Fee, i.Total, i.IsPaid, i.PaidOn))
                .ToList(),
            Repayments = loan.Repayments
                .Select(r => new RepaymentView(
                    r.Kind, r.PaidOn, r.Principal, r.Interest, r.Fee, r.Penalty, r.Total,
                    names.GetValueOrDefault(r.RecordedBy.Value, "Unknown user")))
                .ToList(),
        };
    }

    public async Task<LoanId?> FindByApplicationAsync(
        LoanApplicationId applicationId, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!await IsVisibleAsync(applicationId, access, cancellationToken))
        {
            return null;
        }

        var ids = await db.Loans.Where(l => l.ApplicationId == applicationId).OrderBy(l => l.Id).Select(l => l.Id).Take(1).ToListAsync(cancellationToken);
        return ids.Count == 0 ? null : ids[0];
    }

    private Task<bool> IsVisibleAsync(LoanApplicationId applicationId, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return Task.FromResult(false);
        }

        var applications = db.LoanApplications.Where(a => a.Id == applicationId);
        if (access.Branch is { } branch)
        {
            applications = applications.Where(a => a.BranchId == branch);
        }

        return applications.AnyAsync(cancellationToken);
    }
}
