using CreditFlow.Application.Applications;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Products;

namespace CreditFlow.Application.Reports;

/// <summary>The reports of the task (and the page / export order).</summary>
public enum ReportKind
{
    ApplicationsByStatus = 1,
    ApprovalRate = 2,
    TimePerStage = 3,
    RejectionReasons = 4,
    Portfolio = 5,
    DisbursedVolume = 6,
    Overdue = 7,
    Escalations = 8,
}

/// <summary>The period (bank dates, both inclusive) and an optional product, applied to every report.</summary>
public sealed record ReportFilter(DateOnly From, DateOnly To, LoanProductId? ProductId)
{
    /// <summary>The longest period a report covers.</summary>
    public const int MaxDays = 3 * 366;

    /// <summary>Dates in order and at most <see cref="MaxDays"/> apart (the start moves).</summary>
    public ReportFilter Normalize()
    {
        var (from, to) = From <= To ? (From, To) : (To, From);
        return this with { From = from.DayNumber < to.DayNumber - MaxDays ? to.AddDays(-MaxDays) : from, To = to };
    }
}

public enum ReportColumnKind
{
    Text = 1,
    /// <summary>A whole number.</summary>
    Count = 2,

    /// <summary>GEL, 2 decimals.</summary>
    Amount = 3,

    /// <summary>A share, 0–100, 1 decimal.</summary>
    Percent = 4,

    /// <summary>Hours, 1 decimal.</summary>
    Hours = 5,
}

/// <summary>A column of a report: a stable key (for texts and tests), an English title and how values are shown.</summary>
public sealed record ReportColumn(string Key, string Title, ReportColumnKind Kind);

/// <summary>
/// One report as a table: the single shape behind the page, the Excel sheet and the CSV file, so they always agree.
/// Values are <c>string</c>, <c>int</c> or <c>decimal</c> as the column kind says.
/// </summary>
public sealed record ReportTable(ReportKind Kind, string Title, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<object>> Rows);

/// <summary>Applications created in the period, by their current status.</summary>
public sealed record StatusRow(ApplicationStatus Status, int Applications, decimal AmountGel);

/// <summary>Decisions taken in the period, by product.</summary>
public sealed record DecisionRow(string Product, int Approved, int Rejected);

/// <summary>Average time an application spent in a status it left during the period.</summary>
public sealed record StageTimeRow(ApplicationStatus Stage, int Transitions, decimal AverageHours);

/// <summary>A rejection in the period: where it came from and the recorded reason.</summary>
public sealed record RejectionRow(ApplicationStatus? From, string? Reason);

/// <summary>Active loans today, by product.</summary>
public sealed record PortfolioRow(string Product, int ActiveLoans, decimal OutstandingGel);

/// <summary>Loans disbursed in the period, by product and branch.</summary>
public sealed record DisbursedRow(string Product, string Branch, int Loans, decimal VolumeGel);

/// <summary>Installments past due today and unpaid, by product.</summary>
public sealed record OverdueRow(string Product, int Loans, int Installments, decimal OverdueGel);

/// <summary>Escalations in the period and files still waiting for a senior, by product.</summary>
public sealed record EscalationRow(string Product, int Escalated, int StillWaiting);

/// <summary>
/// The figures behind the reports. Every method applies the viewer's <see cref="ApplicationAccessFilter"/> in SQL
/// (an operator sees the figures of their branch only) and the filter's product.
/// </summary>
public interface IReportQueries
{
    Task<IReadOnlyList<StatusRow>> ApplicationsByStatusAsync(ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken);

    Task<IReadOnlyList<DecisionRow>> DecisionsAsync(ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken);

    Task<IReadOnlyList<StageTimeRow>> TimePerStageAsync(ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken);

    Task<IReadOnlyList<RejectionRow>> RejectionsAsync(ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken);

    Task<IReadOnlyList<PortfolioRow>> PortfolioAsync(ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken);

    Task<IReadOnlyList<DisbursedRow>> DisbursedAsync(ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken);

    /// <param name="today">Installments due before this bank date are overdue.</param>
    /// <param name="filter">Product filter (the period does not apply).</param>
    /// <param name="access">The viewer's scope.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<OverdueRow>> OverdueAsync(DateOnly today, ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken);

    Task<IReadOnlyList<EscalationRow>> EscalationsAsync(ReportFilter filter, ApplicationAccessFilter access, CancellationToken cancellationToken);
}

/// <summary>Writes report tables to an Excel workbook (one sheet per report).</summary>
public interface IReportExcelWriter
{
    byte[] Write(IReadOnlyList<ReportTable> tables, ReportFilter filter);
}
