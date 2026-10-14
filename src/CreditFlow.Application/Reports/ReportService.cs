using System.Globalization;
using System.Text;
using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Application.Disbursements;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Reports;

public enum ExportFormat
{
    Excel = 1,
    Csv = 2,
}

/// <summary>
/// The management reports of the task: applications by status, approval and rejection rates, average time per stage,
/// rejection reasons, the portfolio by product, disbursed volume by product and branch, overdue installments and
/// escalations. Reading needs <c>reports.view</c>, exporting <c>reports.export</c>; both show only what the viewer's
/// data scope allows (computed in SQL), so an export never contains more than the page.
/// </summary>
public sealed class ReportService(
    CurrentAccessProfile access, IReportQueries queries, IReportExcelWriter excel, TimeProvider clock)
{
    private static readonly ReportColumn ProductColumn = new("Product", "Product", ReportColumnKind.Text);

    public async Task<Result<IReadOnlyList<ReportTable>>> BuildAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var allowed = await AccessAsync(Permissions.Reports.View, cancellationToken);
        return allowed.IsFailure
            ? Result.Failure<IReadOnlyList<ReportTable>>(allowed.Error!)
            : Result.Success(await TablesAsync(filter.Normalize(), allowed.Value, cancellationToken));
    }

    /// <summary>
    /// The reports as a file: Excel = every report, one sheet each; CSV = one report (<paramref name="kind"/>).
    /// </summary>
    public async Task<Result<RenderedDocument>> ExportAsync(
        ReportFilter filter, ExportFormat format, ReportKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var allowed = await AccessAsync(Permissions.Reports.Export, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<RenderedDocument>(allowed.Error!);
        }

        var normalized = filter.Normalize();
        var tables = await TablesAsync(normalized, allowed.Value, cancellationToken);
        var period = string.Create(CultureInfo.InvariantCulture, $"{normalized.From:yyyyMMdd}-{normalized.To:yyyyMMdd}");
        if (format == ExportFormat.Excel)
        {
            return Result.Success(new RenderedDocument(
                $"creditflow-reports-{period}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                excel.Write(tables, normalized)));
        }

        var table = tables.Single(t => t.Kind == kind);
        return Result.Success(new RenderedDocument(
            $"creditflow-{kind.ToString().ToLowerInvariant()}-{period}.csv", "text/csv", CsvReportWriter.Write(table)));
    }

    private async Task<Result<ApplicationAccessFilter>> AccessAsync(string permission, CancellationToken cancellationToken)
    {
        var profile = await access.GetAsync(cancellationToken);
        if (profile is null)
        {
            return Result.Failure<ApplicationAccessFilter>(CommonErrors.NotAuthenticated);
        }

        return profile.HasPermission(permission)
            ? Result.Success(ApplicationAccessFilter.For(profile))
            : Result.Failure<ApplicationAccessFilter>(CommonErrors.AccessDenied);
    }

    private async Task<IReadOnlyList<ReportTable>> TablesAsync(
        ReportFilter filter, ApplicationAccessFilter scope, CancellationToken cancellationToken)
    {
        var today = BankTime.DateOf(clock.GetUtcNow());
        return
        [
            ByStatus(await queries.ApplicationsByStatusAsync(filter, scope, cancellationToken)),
            ApprovalRate(await queries.DecisionsAsync(filter, scope, cancellationToken)),
            TimePerStage(await queries.TimePerStageAsync(filter, scope, cancellationToken)),
            RejectionReasons(await queries.RejectionsAsync(filter, scope, cancellationToken)),
            Portfolio(await queries.PortfolioAsync(filter, scope, cancellationToken)),
            Disbursed(await queries.DisbursedAsync(filter, scope, cancellationToken)),
            Overdue(await queries.OverdueAsync(today, filter, scope, cancellationToken)),
            Escalations(await queries.EscalationsAsync(filter, scope, cancellationToken)),
        ];
    }

    private static ReportTable ByStatus(IReadOnlyList<StatusRow> rows) => new(
        ReportKind.ApplicationsByStatus,
        "Applications created in the period, by current status",
        [
            new("Status", "Status", ReportColumnKind.Text),
            new("Applications", "Applications", ReportColumnKind.Count),
            new("AmountGel", "Amount (GEL)", ReportColumnKind.Amount),
        ],
        rows.OrderBy(r => r.Status).Select(r => Row(Words(r.Status.ToString()), r.Applications, r.AmountGel))
            .Append(Row("Total", rows.Sum(r => r.Applications), rows.Sum(r => r.AmountGel))).ToList());

    private static ReportTable ApprovalRate(IReadOnlyList<DecisionRow> rows) => new(
        ReportKind.ApprovalRate,
        "Approval and rejection rate of the decisions in the period",
        [
            ProductColumn,
            new("Approved", "Approved", ReportColumnKind.Count),
            new("Rejected", "Rejected", ReportColumnKind.Count),
            new("ApprovalRate", "Approval rate (%)", ReportColumnKind.Percent),
            new("RejectionRate", "Rejection rate (%)", ReportColumnKind.Percent),
        ],
        rows.OrderBy(r => r.Product, StringComparer.Ordinal)
            .Append(new DecisionRow("All products", rows.Sum(r => r.Approved), rows.Sum(r => r.Rejected)))
            .Select(r => Row(r.Product, r.Approved, r.Rejected, Share(r.Approved, r.Approved + r.Rejected), Share(r.Rejected, r.Approved + r.Rejected)))
            .ToList());

    private static ReportTable TimePerStage(IReadOnlyList<StageTimeRow> rows) => new(
        ReportKind.TimePerStage,
        "Average time per stage (stages left in the period)",
        [
            new("Stage", "Stage", ReportColumnKind.Text),
            new("Transitions", "Applications", ReportColumnKind.Count),
            new("AverageHours", "Average time (hours)", ReportColumnKind.Hours),
        ],
        rows.OrderBy(r => r.Stage).Select(r => Row(Words(r.Stage.ToString()), r.Transitions, r.AverageHours)).ToList());

    private static ReportTable RejectionReasons(IReadOnlyList<RejectionRow> rows)
    {
        var reasons = rows.SelectMany(ReasonsOf).GroupBy(r => r).Select(g => (Reason: g.Key, Count: g.Count())).ToList();
        var total = rows.Count;
        return new ReportTable(
            ReportKind.RejectionReasons,
            "Rejection reasons in the period (a scoring rejection counts every failed rule)",
            [
                new("Reason", "Reason", ReportColumnKind.Text),
                new("Rejections", "Rejections", ReportColumnKind.Count),
                new("Share", "Share of rejected applications (%)", ReportColumnKind.Percent),
            ],
            reasons.OrderByDescending(r => r.Count).ThenBy(r => r.Reason, StringComparer.Ordinal)
                .Select(r => Row(r.Reason, r.Count, Share(r.Count, total))).ToList());
    }

    private static ReportTable Portfolio(IReadOnlyList<PortfolioRow> rows) => new(
        ReportKind.Portfolio,
        "Portfolio today by product (active loans)",
        [
            ProductColumn,
            new("ActiveLoans", "Active loans", ReportColumnKind.Count),
            new("OutstandingGel", "Outstanding principal (GEL)", ReportColumnKind.Amount),
        ],
        rows.OrderBy(r => r.Product, StringComparer.Ordinal).Select(r => Row(r.Product, r.ActiveLoans, r.OutstandingGel))
            .Append(Row("Total", rows.Sum(r => r.ActiveLoans), rows.Sum(r => r.OutstandingGel))).ToList());

    private static ReportTable Disbursed(IReadOnlyList<DisbursedRow> rows) => new(
        ReportKind.DisbursedVolume,
        "Disbursed in the period, by product and branch",
        [
            ProductColumn,
            new("Branch", "Branch", ReportColumnKind.Text),
            new("Loans", "Loans", ReportColumnKind.Count),
            new("VolumeGel", "Volume (GEL)", ReportColumnKind.Amount),
        ],
        rows.OrderBy(r => r.Product, StringComparer.Ordinal).ThenBy(r => r.Branch, StringComparer.Ordinal)
            .Select(r => Row(r.Product, r.Branch, r.Loans, r.VolumeGel))
            .Append(Row("Total", string.Empty, rows.Sum(r => r.Loans), rows.Sum(r => r.VolumeGel))).ToList());

    private static ReportTable Overdue(IReadOnlyList<OverdueRow> rows) => new(
        ReportKind.Overdue,
        "Overdue installments today, by product",
        [
            ProductColumn,
            new("Loans", "Loans in arrears", ReportColumnKind.Count),
            new("Installments", "Overdue installments", ReportColumnKind.Count),
            new("OverdueGel", "Overdue amount (GEL)", ReportColumnKind.Amount),
        ],
        rows.OrderBy(r => r.Product, StringComparer.Ordinal).Select(r => Row(r.Product, r.Loans, r.Installments, r.OverdueGel)).ToList());

    private static ReportTable Escalations(IReadOnlyList<EscalationRow> rows) => new(
        ReportKind.Escalations,
        "Escalations to a Senior Underwriter",
        [
            ProductColumn,
            new("Escalated", "Escalated in the period", ReportColumnKind.Count),
            new("StillWaiting", "Still waiting in Underwriting", ReportColumnKind.Count),
        ],
        rows.OrderBy(r => r.Product, StringComparer.Ordinal).Select(r => Row(r.Product, r.Escalated, r.StillWaiting)).ToList());

    /// <summary>Where a rejection came from; a scoring rejection lists every failed hard rule.</summary>
    private static IEnumerable<string> ReasonsOf(RejectionRow rejection)
    {
        const string ScoringPrefix = "Scoring rule failed:";
        if (rejection.From == ApplicationStatus.Scoring && rejection.Reason?.StartsWith(ScoringPrefix, StringComparison.Ordinal) == true)
        {
            return rejection.Reason[ScoringPrefix.Length..].Trim().TrimEnd('.')
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(code => $"Scoring: {code}");
        }

        return
        [
            rejection.From switch
            {
                ApplicationStatus.PreCheck => "Identity (KYC) not verified",
                ApplicationStatus.ComplianceReview => "Compliance (sanctions / PEP)",
                ApplicationStatus.Underwriting => "Underwriter decision",
                ApplicationStatus.CreditCommittee => "Credit committee decision",
                _ => "Other",
            },
        ];
    }

    private static decimal Share(int part, int whole) => whole == 0 ? 0m : Math.Round(100m * part / whole, 1, MidpointRounding.AwayFromZero);

    private static object[] Row(params object[] values) => values;

    /// <summary>"DocumentsPending" → "Documents pending".</summary>
    private static string Words(string pascalCase)
    {
        var text = new StringBuilder(pascalCase.Length + 8);
        foreach (var c in pascalCase)
        {
            text.Append(text.Length > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString());
        }

        return text.ToString();
    }
}

/// <summary>
/// CSV (RFC 4180, UTF-8 with BOM so Excel detects the encoding): a header row, values in invariant culture, text quoted
/// when needed, and text that a spreadsheet would run as a formula (<c>= + - @</c>, tab, CR) prefixed with an
/// apostrophe (CSV injection).
/// </summary>
public static class CsvReportWriter
{
    public static byte[] Write(ReportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var csv = new StringBuilder();
        csv.Append(string.Join(',', table.Columns.Select(c => Text(c.Title)))).Append("\r\n");
        foreach (var row in table.Rows)
        {
            csv.Append(string.Join(',', row.Select((value, i) => Cell(value, table.Columns[i].Kind)))).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    /// <summary>The value as shown in exports (invariant culture).</summary>
    public static string Format(object value, ReportColumnKind kind) => (value, kind) switch
    {
        (decimal d, ReportColumnKind.Amount) => d.ToString("0.00", CultureInfo.InvariantCulture),
        (decimal d, _) => d.ToString("0.0", CultureInfo.InvariantCulture),
        (int i, _) => i.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static string Cell(object value, ReportColumnKind kind) =>
        kind == ReportColumnKind.Text ? Text(Format(value, kind)) : Format(value, kind);

    private static string Text(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}
