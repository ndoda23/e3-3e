using System.Net;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Application.Reports;
using CreditFlow.IntegrationTests.Infrastructure;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>The reports page and its exports through HTTP: every report shown, export links and downloads by permission.</summary>
public sealed class ReportPagesTests(SqlServerContainer sql) : WebTestBase(sql)
{
    [Fact]
    public async Task An_operator_sees_every_report_without_export_links_and_cannot_download()
    {
        var (userName, _, password) = await CreateUserAsync(RoleCodes.Operator, BranchSeedCatalog.Vake);
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var page = await browser.GetPageAsync("/Reports?from=2026-09-01&to=2026-10-06");

        foreach (var kind in Enum.GetValues<ReportKind>())
        {
            Assert.Contains($"data-report=\"{kind}\"", page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("data-export=", page, StringComparison.Ordinal);
        Assert.Equal("/Account/AccessDenied",
            (await browser.GetAsync("/Reports/Export?format=Csv&report=Portfolio")).Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task A_senior_underwriter_downloads_excel_and_csv()
    {
        var (userName, _, password) = await CreateUserAsync(RoleCodes.SeniorUnderwriter);
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var page = await browser.GetPageAsync("/Reports");
        Assert.Contains("data-export=\"xlsx\"", page, StringComparison.Ordinal);
        Assert.Contains("data-export=\"csv\"", page, StringComparison.Ordinal);

        var excel = await browser.GetAsync("/Reports/Export?format=Excel&report=ApplicationsByStatus&from=2026-09-01&to=2026-10-06");
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", excel.Content.Headers.ContentType?.MediaType);
        Assert.Equal("creditflow-reports-20260901-20261006.xlsx", excel.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("PK"u8.ToArray(), (await excel.Content.ReadAsByteArrayAsync(Ct))[..2]);

        var csv = await browser.GetAsync("/Reports/Export?format=Csv&report=RejectionReasons");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/Reports/Export?format=Pdf&report=Portfolio")).StatusCode);
    }
}
