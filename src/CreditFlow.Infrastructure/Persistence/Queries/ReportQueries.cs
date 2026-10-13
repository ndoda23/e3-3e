using CreditFlow.Application.Applications;
using CreditFlow.Application.Reports;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Loans;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Queries;

/// <summary>
/// Report figures. Every query starts from the applications the viewer may see (<see cref="Visible"/>: branch scope and
/// product filter in SQL) and aggregates in the database; only small, already-filtered row sets are finished in memory.
/// Amounts are GEL at the rate fixed when each application was scored.
/// </summary>
internal sealed class ReportQueries(CreditFlowDbContext db) : IReportQueries
{
    public async Task<IReadOnlyList<StatusRow>> ApplicationsByStatusAsync(
        ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return [];
        }

        var (start, end) = Period(filter);
        var rows = await Visible(filter, access)
            .Where(a => a.CreatedAt >= start && a.CreatedAt < end)
            .Select(a => new
            {
                a.Status,
                AmountGel = a.AmountGel ?? (a.Terms.Amount.Currency == Currency.GEL ? a.Terms.Amount.Amount : 0m),
            })
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(a => a.AmountGel) })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new StatusRow(r.Status, r.Count, r.Amount)).ToList();
    }

    public async Task<IReadOnlyList<DecisionRow>> DecisionsAsync(
        ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return [];
        }

        var (start, end) = Period(filter);
        var rows = await (
                from a in Visible(filter, access)
                join p in db.LoanProducts on a.ProductId equals p.Id
                from h in a.History
                where h.At >= start && h.At < end
                      && (h.To == ApplicationStatus.Approved || h.To == ApplicationStatus.ConditionallyApproved || h.To == ApplicationStatus.Rejected)
                group h by p.Name into g
                select new
                {
                    Product = g.Key,
                    Approved = g.Count(h => h.To != ApplicationStatus.Rejected),
                    Rejected = g.Count(h => h.To == ApplicationStatus.Rejected),
                })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new DecisionRow(r.Product, r.Approved, r.Rejected)).ToList();
    }

    /// <summary>
    /// The time between entering a status and leaving it, for the transitions that left a status in the period: the next
    /// history entry of the same application (<c>LEAD</c>, parameterised SQL) marks when the status was left.
    /// </summary>
    public async Task<IReadOnlyList<StageTimeRow>> TimePerStageAsync(
        ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return [];
        }

        var (start, end) = Period(filter);
        var allBranches = access.Branch is null ? 1 : 0;
        var branch = access.Branch?.Value ?? Guid.Empty;
        var allProducts = filter.ProductId is null ? 1 : 0;
        var product = filter.ProductId?.Value ?? Guid.Empty;
        var rows = await db.Database.SqlQuery<StageTime>($"""
            SELECT s.[To] AS [Stage], COUNT(*) AS [Transitions],
                   CAST(AVG(CAST(DATEDIFF(SECOND, s.[At], s.[NextAt]) AS decimal(19, 2))) / 3600.0 AS decimal(19, 1)) AS [AverageHours]
            FROM (
                SELECT h.[To], h.[At], LEAD(h.[At]) OVER (PARTITION BY h.[ApplicationId] ORDER BY h.[Id]) AS [NextAt]
                FROM [ApplicationStatusHistory] AS h
                INNER JOIN [LoanApplications] AS a ON a.[Id] = h.[ApplicationId]
                WHERE ({allBranches} = 1 OR a.[BranchId] = {branch}) AND ({allProducts} = 1 OR a.[ProductId] = {product})
            ) AS s
            WHERE s.[NextAt] IS NOT NULL AND s.[NextAt] >= {start} AND s.[NextAt] < {end}
            GROUP BY s.[To]
            """).ToListAsync(cancellationToken);
        return rows.Select(r => new StageTimeRow(Enum.Parse<ApplicationStatus>(r.Stage), r.Transitions, r.AverageHours)).ToList();
    }

    public async Task<IReadOnlyList<RejectionRow>> RejectionsAsync(
        ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return [];
        }

        var (start, end) = Period(filter);
        return await Visible(filter, access)
            .SelectMany(a => a.History
                .Where(h => h.To == ApplicationStatus.Rejected && h.At >= start && h.At < end)
                .Select(h => new RejectionRow(h.From, h.Reason)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PortfolioRow>> PortfolioAsync(
        ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return [];
        }

        var rows = await (
                from l in db.Loans.AsNoTracking()
                where l.Status == LoanStatus.Active
                join a in Visible(filter, access) on l.ApplicationId equals a.Id
                join p in db.LoanProducts on l.ProductId equals p.Id
                group new { l.OutstandingPrincipal.Amount, a.AmountGel, Principal = a.Terms.Amount.Amount } by p.Name into g
                select new
                {
                    Product = g.Key,
                    Loans = g.Count(),
                    Outstanding = g.Sum(x => x.Amount * (x.AmountGel ?? x.Principal) / x.Principal),
                })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new PortfolioRow(r.Product, r.Loans, Money.Round(r.Outstanding))).ToList();
    }

    public async Task<IReadOnlyList<DisbursedRow>> DisbursedAsync(
        ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return [];
        }

        var rows = await (
                from l in db.Loans.AsNoTracking()
                where l.DisbursedOn >= filter.From && l.DisbursedOn <= filter.To
                join a in Visible(filter, access) on l.ApplicationId equals a.Id
                join p in db.LoanProducts on l.ProductId equals p.Id
                join b in db.Branches on a.BranchId equals b.Id
                group new { a.AmountGel, Principal = a.Terms.Amount.Amount } by new { Product = p.Name, Branch = b.Name } into g
                select new
                {
                    g.Key.Product,
                    g.Key.Branch,
                    Loans = g.Count(),
                    Volume = g.Sum(x => x.AmountGel ?? x.Principal),
                })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new DisbursedRow(r.Product, r.Branch, r.Loans, Money.Round(r.Volume))).ToList();
    }

    public async Task<IReadOnlyList<OverdueRow>> OverdueAsync(
        DateOnly today, ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return [];
        }

        var installments = await (
                from l in db.Loans.AsNoTracking()
                where l.Status == LoanStatus.Active
                join a in Visible(filter, access) on l.ApplicationId equals a.Id
                join p in db.LoanProducts on l.ProductId equals p.Id
                from i in l.Installments
                where !i.IsPaid && i.DueDate < today
                select new
                {
                    Product = p.Name,
                    LoanId = l.Id,
                    Amount = (i.Principal.Amount + i.Interest.Amount + i.Fee.Amount) * (a.AmountGel ?? a.Terms.Amount.Amount) / a.Terms.Amount.Amount,
                })
            .ToListAsync(cancellationToken);
        return installments
            .GroupBy(i => i.Product, StringComparer.Ordinal)
            .Select(g => new OverdueRow(g.Key, g.Select(i => i.LoanId).Distinct().Count(), g.Count(), Money.Round(g.Sum(i => i.Amount))))
            .ToList();
    }

    public async Task<IReadOnlyList<EscalationRow>> EscalationsAsync(
        ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return [];
        }

        var (start, end) = Period(filter);
        var rows = await (
                from a in Visible(filter, access)
                where a.EscalatedAt != null && ((a.EscalatedAt >= start && a.EscalatedAt < end) || a.Status == ApplicationStatus.Underwriting)
                join p in db.LoanProducts on a.ProductId equals p.Id
                group a by p.Name into g
                select new
                {
                    Product = g.Key,
                    Escalated = g.Count(a => a.EscalatedAt >= start && a.EscalatedAt < end),
                    Waiting = g.Count(a => a.Status == ApplicationStatus.Underwriting),
                })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new EscalationRow(r.Product, r.Escalated, r.Waiting)).ToList();
    }

    /// <summary>The viewer's applications (branch scope) of the filtered product, in SQL.</summary>
    private IQueryable<LoanApplication> Visible(ReportFilter filter, ApplicationAccessFilter access)
    {
        var applications = db.LoanApplications.AsNoTracking();
        if (access.Branch is { } branch)
        {
            applications = applications.Where(a => a.BranchId == branch);
        }

        if (filter.ProductId is { } product)
        {
            applications = applications.Where(a => a.ProductId == product);
        }

        return applications;
    }

    /// <summary>The period as instants: from the start of the first bank day to the start of the day after the last.</summary>
    private static (DateTimeOffset From, DateTimeOffset To) Period(ReportFilter filter) =>
        (BankTime.ToInstant(filter.From, TimeOnly.MinValue), BankTime.ToInstant(filter.To.AddDays(1), TimeOnly.MinValue));

    /// <summary>A row of the stage-time SQL.</summary>
    private sealed class StageTime
    {
        public string Stage { get; set; } = string.Empty;

        public int Transitions { get; set; }

        [Precision(19, 1)] // matches the CAST in the SQL
        public decimal AverageHours { get; set; }
    }
}
