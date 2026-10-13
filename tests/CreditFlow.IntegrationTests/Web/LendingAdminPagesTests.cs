using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Common;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Admin › Products, Scoring rules, Approval thresholds, FX rates and Holidays through HTTP.</summary>
public sealed partial class LendingAdminPagesTests(SqlServerContainer sql) : WebTestBase(sql)
{
    [GeneratedRegex("/Admin/Products/Details/(?<id>[0-9a-f-]{36})")]
    private static partial Regex ProductLocation();

    [Fact]
    public async Task Every_admin_page_renders_for_the_administrator()
    {
        var (admin, _) = await SignInAsAdministratorAsync();
        using var _browser = admin;
        var consumer = await QueryAsync(db => db.LoanProducts.Where(p => p.Code == LendingSeedCatalog.Consumer).Select(p => p.Id.Value).SingleAsync(Ct));

        foreach (var path in new[]
        {
            "/Admin/Products", "/Admin/Products/Create", $"/Admin/Products/Details/{consumer}", "/Admin/ScoringRules",
            "/Admin/ScoringRules?version=1", "/Admin/ApprovalThresholds", "/Admin/FxRates", "/Admin/FxRates?currency=USD",
            "/Admin/Holidays?year=2026",
        })
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(path)).StatusCode);
        }

        var menu = await admin.GetPageAsync("/Admin/Products");
        foreach (var item in new[] { "Products", "ScoringRules", "ApprovalThresholds", "FxRates", "Holidays" })
        {
            Assert.Contains($"data-nav=\"{item}\"", menu, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_product_is_created_and_a_new_version_published_through_the_forms()
    {
        var (admin, _) = await SignInAsAdministratorAsync();
        using var _browser = admin;
        var fields = TermFields("Terms.");
        fields["Code"] = "EDU";
        fields["Name"] = "Education loan";
        fields["Type"] = "Consumer";

        var created = await admin.PostFormAsync("/Admin/Products/Create", "/Admin/Products/Create", fields);

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var id = ProductLocation().Match(created.Headers.Location!.OriginalString).Groups["id"].Value;
        var next = TermFields(string.Empty);
        next["MaxRate"] = "19";
        var published = await admin.PostFormAsync($"/Admin/Products/Details/{id}", $"/Admin/Products/PublishVersion/{id}", next);
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);

        var page = await admin.GetPageAsync($"/Admin/Products/Details/{id}");
        Assert.Contains("data-version=\"2\"", page, StringComparison.Ordinal);
        Assert.Contains("data-version=\"1\"", page, StringComparison.Ordinal);

        // Invalid terms come back on the form with the error, and nothing is published.
        next["MinAmountGel"] = "999999";
        var refused = await admin.PostFormAsync($"/Admin/Products/Details/{id}", $"/Admin/Products/PublishVersion/{id}", next);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("validation-summary-errors", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.DoesNotContain("data-version=\"3\"", await admin.GetPageAsync($"/Admin/Products/Details/{id}"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fx_rates_are_imported_from_a_csv_upload()
    {
        var (admin, _) = await SignInAsAdministratorAsync();
        using var _browser = admin;
        var token = WebSession.Token(await admin.GetPageAsync("/Admin/FxRates"));

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("Currency,EffectiveDate,GelPerUnit\nUSD,2026-10-05,2.7111\nEUR,2026-10-05,3.1222\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "rates.csv");
        var response = await admin.Client.PostAsync("/Admin/FxRates/Import", form, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("data-fx=\"USD-2026-10-05\"", await admin.GetPageAsync("/Admin/FxRates"), StringComparison.Ordinal);
        Assert.Equal(3.1222m, await QueryAsync(db => db.FxRates
            .Where(r => r.Currency == Currency.EUR && r.EffectiveDate == new DateOnly(2026, 10, 5)).Select(r => r.GelPerUnit).SingleAsync(Ct)));
    }

    [Theory]
    [InlineData(RoleCodes.Operator)]
    [InlineData(RoleCodes.SeniorUnderwriter)]
    [InlineData(RoleCodes.Auditor)]
    public async Task Business_users_cannot_open_the_reference_data_admin(string roleCode)
    {
        var (userName, _, password) = await CreateUserAsync(roleCode, roleCode == RoleCodes.Operator ? BranchSeedCatalog.Vake : null);
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        foreach (var path in new[] { "/Admin/Products", "/Admin/ScoringRules", "/Admin/ApprovalThresholds", "/Admin/FxRates", "/Admin/Holidays" })
        {
            Assert.Equal("/Account/AccessDenied", (await browser.GetAsync(path)).Headers.Location!.AbsolutePath);
        }
    }

    /// <summary>Valid consumer-like terms as form fields (with the given name prefix).</summary>
    private static Dictionary<string, string> TermFields(string prefix)
    {
        var fields = new Dictionary<string, string>
        {
            ["MinAmountGel"] = "1000",
            ["MaxAmountGel"] = "30000",
            ["MinTermMonths"] = "6",
            ["MaxTermMonths"] = "48",
            ["MinRate"] = "12",
            ["MaxRate"] = "20",
            ["UpfrontFeePercent"] = "1",
            ["MonthlyFeePercent"] = "0",
            ["EarlyRepaymentPenaltyPercent"] = "2",
            ["Currencies"] = "GEL",
            ["ScheduleTypes"] = "Annuity",
            ["Documents[0].Type"] = "IdentityDocumentCopy",
            ["Documents[0].Required"] = "true",
        };
        return fields.ToDictionary(f => prefix + f.Key, f => f.Value);
    }
}
