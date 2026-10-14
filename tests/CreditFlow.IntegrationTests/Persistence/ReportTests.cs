using System.Text;
using ClosedXML.Excel;
using CreditFlow.Application.Applications;
using CreditFlow.Application.Common;
using CreditFlow.Application.Disbursements;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Application.Reports;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Outbox;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CreditFlow.IntegrationTests.Persistence;

/// <summary>
/// The reports on a known data set in two branches: the figures, the branch scope (an operator sees their branch, the
/// auditor the bank) on the page model and in both exports, and the permissions.
/// </summary>
public sealed class ReportTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    // Thursday 1 October 2026, 10:00 in Tbilisi.
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly ReportFilter October = new(Start, new DateOnly(2026, 11, 3), null);

    private readonly FakeTimeProvider _clock = new(BankTime.ToInstant(Start, new TimeOnly(10, 0)));

    protected override string Environment => "Development";

    protected override bool SeedDevUsers => true;

    protected override TimeProvider? Clock => _clock;

    [Fact]
    public async Task An_operator_gets_the_figures_of_their_branch_and_the_auditor_those_of_the_bank()
    {
        await SeedAsync();

        var vake = await TablesAsync("anna.gelashvili");
        var bank = await TablesAsync("levan.abashidze");

        // Applications created in the period: Vake has the disbursed, the rejected and the escalated one.
        Assert.Equal([("Underwriting", 1), ("Active", 1), ("Rejected", 1), ("Total", 3)], Counts(vake[ReportKind.ApplicationsByStatus])); // process order
        Assert.Equal(4, (int)Row(bank[ReportKind.ApplicationsByStatus], "Total")[1]);

        // One approval and one rejection in Vake; the bank adds Batumi's approval.
        Assert.Equal<object>([LendingName, 1, 1, 50.0m, 50.0m], Row(vake[ReportKind.ApprovalRate], LendingName));
        Assert.Equal<object>(["All products", 2, 1, 66.7m, 33.3m], Row(bank[ReportKind.ApprovalRate], "All products"));

        Assert.Equal<object>(["Underwriter decision", 1, 100.0m], Row(vake[ReportKind.RejectionReasons], "Underwriter decision"));

        Assert.Equal<object>([LendingName, 1, 10_000.00m], Row(vake[ReportKind.Portfolio], LendingName));
        Assert.Equal<object>(["Total", 2, 20_000.00m], Row(bank[ReportKind.Portfolio], "Total"));

        Assert.Equal(["Vake"], Branches(vake[ReportKind.DisbursedVolume]));
        Assert.Equal(["Batumi", "Vake"], Branches(bank[ReportKind.DisbursedVolume]));
        Assert.Equal(10_000.00m, (decimal)Row(vake[ReportKind.DisbursedVolume], LendingName)[3]);

        // The first installments fell due on 1 November and are unpaid.
        var overdue = Row(vake[ReportKind.Overdue], LendingName);
        Assert.Equal((1, 1), ((int)overdue[1], (int)overdue[2]));
        Assert.True((decimal)overdue[3] > 0m);
        Assert.Equal(2, (int)Row(bank[ReportKind.Overdue], LendingName)[1]);

        Assert.Equal<object>([LendingName, 1, 1], Row(vake[ReportKind.Escalations], LendingName));

        var stages = vake[ReportKind.TimePerStage].Rows.Select(r => (string)r[0]).ToList();
        Assert.Contains("Underwriting", stages);
        Assert.Contains("Conditions met", stages);
    }

    [Fact]
    public async Task Exports_contain_exactly_what_the_viewer_may_see()
    {
        await SeedAsync();
        var senior = await UserAsync("tamta.tatishvili");
        var auditor = await UserAsync("levan.abashidze");

        var csv = await ExportAsync(auditor, ExportFormat.Csv, ReportKind.DisbursedVolume);
        var text = Encoding.UTF8.GetString(csv.Content);
        Assert.Equal("text/csv", csv.ContentType);
        Assert.StartsWith("﻿Product,Branch,Loans,Volume (GEL)\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Consumer loan,Batumi,1,10000.00", text, StringComparison.Ordinal);
        Assert.Contains("Consumer loan,Vake,1,10000.00", text, StringComparison.Ordinal);

        var excel = await ExportAsync(senior, ExportFormat.Excel, ReportKind.DisbursedVolume);
        using var workbook = new XLWorkbook(new MemoryStream(excel.Content));
        Assert.Equal(Enum.GetNames<ReportKind>(), workbook.Worksheets.Select(w => w.Name));
        var sheet = workbook.Worksheet(nameof(ReportKind.Portfolio));
        Assert.Equal("Product", sheet.Cell(4, 1).GetString());
        Assert.Equal(2, sheet.Cell(6, 2).GetValue<int>()); // the total row: two active loans in the bank
        Assert.Equal(20_000d, sheet.Cell(6, 3).GetValue<double>());
    }

    [Fact]
    public async Task An_operator_views_reports_of_their_branch_but_cannot_export_and_compliance_sees_none()
    {
        await SeedAsync();
        var operatorId = await UserAsync("anna.gelashvili");
        var compliance = await UserAsync("nino.kvaratskhelia");

        var export = await Host.InScopeAsAsync(operatorId, sp =>
            sp.GetRequiredService<ReportService>().ExportAsync(October, ExportFormat.Csv, ReportKind.Portfolio, Ct));
        var view = await Host.InScopeAsAsync(compliance, sp => sp.GetRequiredService<ReportService>().BuildAsync(October, Ct));

        Assert.Equal(CommonErrors.AccessDenied, export.Error);
        Assert.Equal(CommonErrors.AccessDenied, view.Error);
    }

    [Fact]
    public void Csv_quotes_text_neutralises_formulas_and_formats_numbers_invariantly()
    {
        var table = new ReportTable(
            ReportKind.RejectionReasons,
            "Test",
            [new("Reason", "Reason", ReportColumnKind.Text), new("Count", "Count", ReportColumnKind.Count), new("Share", "Share (%)", ReportColumnKind.Percent)],
            [
                ["=HYPERLINK(\"http://x\")", 1, 12.25m],
                ["Comma, and \"quotes\"", 2, 0m],
                ["-2+3", 3, 100m],
            ]);

        var text = Encoding.UTF8.GetString(CsvReportWriter.Write(table)).TrimStart('﻿');

        Assert.Equal(
            "Reason,Count,Share (%)\r\n" +
            "\"'=HYPERLINK(\"\"http://x\"\")\",1,12.3\r\n" +
            "\"Comma, and \"\"quotes\"\"\",2,0.0\r\n" +
            "'-2+3,3,100.0\r\n",
            text);
    }

    // ----- Data -----

    private const string LendingName = "Consumer loan";

    /// <summary>
    /// Vake: one loan disbursed, one application rejected by an underwriter, one escalated and still waiting.
    /// Batumi: one loan disbursed. Then the clock moves past the first due date (1 November).
    /// </summary>
    private async Task SeedAsync()
    {
        var lending = new TestLending(Host.Services, _clock);
        var anna = await UserAsync("anna.gelashvili");
        var davit = await UserAsync("davit.maisuradze");
        var tamta = await UserAsync("tamta.tatishvili");
        var nika = await UserAsync("nika.beridze");

        var vakeLoan = await lending.ApprovedAsync(LendingSeedCatalog.Consumer, 10_000m, anna, [tamta]);
        await DisburseAsync(anna, vakeLoan);
        var rejected = await lending.InReviewAsync(LendingSeedCatalog.Consumer, 10_000m, anna);
        Assert.True((await Host.InScopeAsAsync(nika, sp => sp.GetRequiredService<DecisionService>().RejectAsync(rejected, "Income not stable.", Ct))).IsSuccess);
        await lending.InReviewAsync(LendingSeedCatalog.Consumer, 10_000m, anna);

        lending.BranchCode = BranchSeedCatalog.Batumi;
        var batumiLoan = await lending.ApprovedAsync(LendingSeedCatalog.Consumer, 10_000m, davit, [tamta]);
        await DisburseAsync(davit, batumiLoan);
        await Host.Services.GetRequiredService<OutboxProcessor>().ProcessAllDueAsync(Ct);

        _clock.SetUtcNow(BankTime.ToInstant(new DateOnly(2026, 10, 6), new TimeOnly(12, 0)));
        Assert.Equal(1, await Host.InScopeAsync(sp => sp.GetRequiredService<ApplicationLifecycleJobs>().EscalateOverdueAsync(Ct)));
        _clock.SetUtcNow(BankTime.ToInstant(October.To, new TimeOnly(12, 0)));
    }

    private async Task DisburseAsync(UserId operatorId, LoanApplicationId id)
    {
        Assert.True((await Host.InScopeAsAsync(operatorId, sp => sp.GetRequiredService<ContractService>().SignAsync(id, Ct))).IsSuccess);
        var paid = await Host.InScopeAsAsync(operatorId, sp => sp.GetRequiredService<DisbursementService>().DisburseAsync(id, Ct));
        Assert.Equal(DisbursementStatus.Succeeded, paid.Value);
    }

    private async Task<Dictionary<ReportKind, ReportTable>> TablesAsync(string userName)
    {
        var user = await UserAsync(userName);
        var tables = await Host.InScopeAsAsync(user, sp => sp.GetRequiredService<ReportService>().BuildAsync(October, Ct));
        Assert.True(tables.IsSuccess, tables.Error?.Description);
        return tables.Value.ToDictionary(t => t.Kind);
    }

    private async Task<RenderedDocument> ExportAsync(UserId user, ExportFormat format, ReportKind kind)
    {
        var file = await Host.InScopeAsAsync(user, sp => sp.GetRequiredService<ReportService>().ExportAsync(October, format, kind, Ct));
        Assert.True(file.IsSuccess, file.Error?.Description);
        return file.Value;
    }

    private Task<UserId> UserAsync(string userName) =>
        Host.QueryAsync(async db => new UserId(
            await db.UserDirectory.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(Ct)));

    private static IReadOnlyList<object> Row(ReportTable table, string first) => table.Rows.Single(r => (string)r[0] == first);

    private static List<(string, int)> Counts(ReportTable table) => table.Rows.Select(r => ((string)r[0], (int)r[1])).ToList();

    private static List<string> Branches(ReportTable table) =>
        table.Rows.Where(r => (string)r[0] != "Total").Select(r => (string)r[1]).Order(StringComparer.Ordinal).ToList();
}
