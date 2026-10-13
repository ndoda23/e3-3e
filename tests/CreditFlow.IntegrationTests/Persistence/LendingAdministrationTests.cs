using CreditFlow.Application.Administration;
using CreditFlow.Application.Common;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Calendar;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Users;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CreditFlow.IntegrationTests.Persistence;

/// <summary>
/// Admin of the lending reference data on SQL Server: products with versions, scoring rule-set versions, approval
/// thresholds, FX rates (incl. CSV import) and holidays. Every change is audited; users without the permission change
/// nothing.
/// </summary>
public sealed class LendingAdministrationTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    private readonly FakeTimeProvider _clock = new(BankTime.ToInstant(new DateOnly(2026, 10, 13), new TimeOnly(9, 0)));

    protected override string Environment => "Development";

    protected override bool SeedDevUsers => true;

    protected override TimeProvider? Clock => _clock;

    // ----- Products -----

    [Fact]
    public async Task A_new_version_of_a_product_applies_to_new_submissions_while_open_applications_keep_their_snapshot()
    {
        var admin = await UserAsync(InfrastructureHost.AdminUserName);
        var lending = new TestLending(Host.Services, _clock);
        var submitted = await lending.InReviewAsync(LendingSeedCatalog.Consumer, 10_000m, await UserAsync("anna.gelashvili"));
        var product = await Host.QueryAsync(db => db.LoanProducts.Include(p => p.Versions).SingleAsync(p => p.Code == LendingSeedCatalog.Consumer, Ct));
        var terms = product.CurrentVersion.Terms with { MaxRate = 22m };

        var published = await AsAsync<ProductAdministrationService, Result>(admin, s => s.PublishVersionAsync(product.Id, terms, Ct));

        Assert.True(published.IsSuccess, published.Error?.Description);
        var details = (await AsAsync<ProductAdministrationService, ProductAdminDetails?>(admin, s => s.GetAsync(product.Id, Ct)))!;
        Assert.Equal([3, 2, 1], details.Versions.Select(v => v.Number));
        Assert.Equal(22m, details.Versions[0].Terms.MaxRate);
        var snapshot = await Host.QueryAsync(db => db.LoanApplications.Where(a => a.Id == submitted).Select(a => a.Snapshot).SingleAsync(Ct));
        Assert.Equal(2, snapshot!.Version);
        Assert.Equal(24m, snapshot.Terms.MaxRate);
        Assert.True(await AuditedAsync("LoanProductVersion"));

        var again = await AsAsync<ProductAdministrationService, Result>(admin, s => s.PublishVersionAsync(product.Id, terms, Ct));
        Assert.Equal(ProductErrors.NothingChanged, again.Error);
    }

    [Fact]
    public async Task A_product_is_created_with_version_one_and_invalid_terms_are_refused()
    {
        var admin = await UserAsync(InfrastructureHost.AdminUserName);
        var terms = new ProductTerms
        {
            MinAmountGel = 1_000m,
            MaxAmountGel = 30_000m,
            MinTermMonths = 6,
            MaxTermMonths = 48,
            MinRate = 12m,
            MaxRate = 20m,
            Currencies = [Currency.GEL],
            ScheduleTypes = [ScheduleType.Annuity],
            RequiredDocuments = [new DocumentRequirement(DocumentType.IdentityDocumentCopy)],
        };

        var created = await AsAsync<ProductAdministrationService, Result<LoanProductId>>(admin, s =>
            s.CreateAsync(new CreateProductCommand("EDU", "Education loan", ProductType.Consumer, terms), Ct));
        var duplicate = await AsAsync<ProductAdministrationService, Result<LoanProductId>>(admin, s =>
            s.CreateAsync(new CreateProductCommand("EDU", "Another", ProductType.Consumer, terms), Ct));
        var invalid = await AsAsync<ProductAdministrationService, Result<LoanProductId>>(admin, s =>
            s.CreateAsync(new CreateProductCommand("BAD", "Bad", ProductType.Consumer, terms with { MinAmountGel = 50_000m }), Ct));

        Assert.True(created.IsSuccess, created.Error?.Description);
        Assert.Equal(ProductErrors.DuplicateCode, duplicate.Error);
        Assert.True(invalid.IsFailure);
        Assert.Contains((await AsAsync<ProductAdministrationService, IReadOnlyList<ProductAdminItem>>(admin, s => s.ListAsync(Ct))),
            p => p.Code == "EDU" && p.CurrentVersion == 1 && p.IsActive);
        Assert.False(await Host.QueryAsync(db => db.LoanProducts.AnyAsync(p => p.Code == "BAD", Ct)));
        Assert.True(await AuditedAsync("LoanProduct"));
    }

    // ----- Scoring rules -----

    [Fact]
    public async Task Rule_parameters_change_as_a_new_rule_set_version_and_invalid_parameters_publish_nothing()
    {
        var admin = await UserAsync(InfrastructureHost.AdminUserName);
        var current = (await Host.QueryAsync(db => db.ScoringRuleSets.OrderByDescending(s => s.Version).FirstAsync(Ct))).Rules;
        var stricter = current
            .Select(r => r.Code == ExposureLimitRule.RuleCode ? r with { ParametersJson = """{"limitGel":250000}""" } : r)
            .ToList();

        var published = await AsAsync<ScoringRuleAdministrationService, Result<int>>(admin, s => s.PublishAsync(stricter, Ct));
        var invalid = await AsAsync<ScoringRuleAdministrationService, Result<int>>(admin, s =>
            s.PublishAsync([.. current.Where(r => r.Code != "PTI"), new RuleDefinition("PTI", true, """{"bands":[]}""")], Ct));
        var unknown = await AsAsync<ScoringRuleAdministrationService, Result<int>>(admin, s =>
            s.PublishAsync([.. current, new RuleDefinition("NO_SUCH_RULE", false, "{}")], Ct));
        var unchanged = await AsAsync<ScoringRuleAdministrationService, Result<int>>(admin, s => s.PublishAsync(stricter, Ct));

        Assert.Equal(2, published.Value);
        Assert.True(invalid.IsFailure);
        Assert.True(unknown.IsFailure);
        Assert.Equal(AdministrationErrors.NothingChanged, unchanged.Error);
        var latest = await Host.QueryAsync(db => db.ScoringRuleSets.OrderByDescending(s => s.Version).FirstAsync(Ct));
        Assert.Equal(2, latest.Version);
        Assert.Equal(250_000m, ExposureLimitRule.LimitOf(latest));
        Assert.True(await AuditedAsync("ScoringRuleSet"));
    }

    // ----- Approval thresholds -----

    [Fact]
    public async Task Approval_thresholds_change_and_route_new_scoring_by_the_new_limits()
    {
        var admin = await UserAsync(InfrastructureHost.AdminUserName);

        var changed = await AsAsync<ApprovalThresholdsAdministrationService, Result>(admin, s => s.ChangeAsync(15_000m, 80_000m, 2, Ct));
        var invalid = await AsAsync<ApprovalThresholdsAdministrationService, Result>(admin, s => s.ChangeAsync(90_000m, 80_000m, 2, Ct));

        Assert.True(changed.IsSuccess, changed.Error?.Description);
        Assert.True(invalid.IsFailure);
        var thresholds = await Host.QueryAsync(db => db.ApprovalThresholds.SingleAsync(Ct));
        Assert.Equal((15_000m, 80_000m), (thresholds.UnderwriterMaxGel, thresholds.SeniorUnderwriterMaxGel));
        Assert.Equal(ApprovalTier.SeniorUnderwriter, thresholds.TierFor(20_000m));
        Assert.True(await AuditedAsync("ApprovalThresholds"));
    }

    // ----- FX rates -----

    [Fact]
    public async Task Fx_rates_are_set_changed_and_deleted_by_hand()
    {
        var admin = await UserAsync(InfrastructureHost.AdminUserName);
        var day = new DateOnly(2026, 10, 13);

        Assert.True((await AsAsync<FxRateAdministrationService, Result>(admin, s => s.SetAsync(Currency.USD, day, 2.71m, Ct))).IsSuccess);
        Assert.True((await AsAsync<FxRateAdministrationService, Result>(admin, s => s.SetAsync(Currency.USD, day, 2.72m, Ct))).IsSuccess);
        Assert.Equal(2.72m, await RateAsync(Currency.USD, day));
        Assert.True((await AsAsync<FxRateAdministrationService, Result>(admin, s => s.SetAsync(Currency.GEL, day, 1m, Ct))).IsFailure);

        Assert.True((await AsAsync<FxRateAdministrationService, Result>(admin, s => s.DeleteAsync(Currency.USD, day, Ct))).IsSuccess);
        Assert.Null(await RateAsync(Currency.USD, day));
        Assert.True(await AuditedAsync("FxRate"));
    }

    [Fact]
    public async Task A_csv_import_is_all_or_nothing()
    {
        var admin = await UserAsync(InfrastructureHost.AdminUserName);

        var imported = await AsAsync<FxRateAdministrationService, Result<int>>(admin, s => s.ImportAsync(
            "Currency,EffectiveDate,GelPerUnit\nUSD,2026-10-13,2.7150\nEUR,2026-10-13,3.1700\nUSD,2026-10-01,2.7200\n", Ct));
        var rejected = await AsAsync<FxRateAdministrationService, Result<int>>(admin, s => s.ImportAsync(
            "USD,2026-10-14,2.7300\nEUR,2026-10-14,0\n", Ct));

        Assert.Equal(3, imported.Value);
        Assert.Equal(2.72m, await RateAsync(Currency.USD, new DateOnly(2026, 10, 1))); // the seeded rate was changed
        Assert.Equal(3.17m, await RateAsync(Currency.EUR, new DateOnly(2026, 10, 13)));
        Assert.True(rejected.IsFailure);
        Assert.StartsWith("Line 2", rejected.Error!.Description, StringComparison.Ordinal);
        Assert.Null(await RateAsync(Currency.USD, new DateOnly(2026, 10, 14)));
    }

    // ----- Holidays -----

    [Fact]
    public async Task Holidays_are_added_renamed_and_removed()
    {
        var admin = await UserAsync(InfrastructureHost.AdminUserName);
        var day = new DateOnly(2026, 12, 31);

        Assert.True((await AsAsync<HolidayAdministrationService, Result>(admin, s => s.AddAsync(day, "Bank day off", Ct))).IsSuccess);
        Assert.Equal(CalendarErrors.DuplicateHoliday, (await AsAsync<HolidayAdministrationService, Result>(admin, s => s.AddAsync(day, "Again", Ct))).Error);
        Assert.True((await AsAsync<HolidayAdministrationService, Result>(admin, s => s.RenameAsync(day, "New Year's Eve", Ct))).IsSuccess);
        Assert.Contains(await AsAsync<HolidayAdministrationService, IReadOnlyList<HolidayItem>>(admin, s => s.ListAsync(2026, Ct)),
            h => h.Date == day && h.Name == "New Year's Eve");

        Assert.True((await AsAsync<HolidayAdministrationService, Result>(admin, s => s.RemoveAsync(day, Ct))).IsSuccess);
        Assert.False(await Host.QueryAsync(db => db.Holidays.AnyAsync(h => h.Date == day, Ct)));
        Assert.True(await AuditedAsync("Holiday"));
    }

    // ----- Permissions -----

    [Theory]
    [InlineData("anna.gelashvili")]
    [InlineData("tamta.tatishvili")]
    [InlineData("levan.abashidze")]
    public async Task Business_users_change_no_reference_data(string userName)
    {
        var user = await UserAsync(userName);
        var product = await Host.QueryAsync(db => db.LoanProducts.Include(p => p.Versions).SingleAsync(p => p.Code == LendingSeedCatalog.Consumer, Ct));
        var rules = (await Host.QueryAsync(db => db.ScoringRuleSets.OrderBy(s => s.Version).FirstAsync(Ct))).Rules;
        var day = new DateOnly(2026, 12, 30);

        var results = new List<Error?>
        {
            (await AsAsync<ProductAdministrationService, Result>(user, s => s.PublishVersionAsync(product.Id, product.CurrentVersion.Terms with { MaxRate = 21m }, Ct))).Error,
            (await AsAsync<ProductAdministrationService, Result>(user, s => s.SetActiveAsync(product.Id, false, Ct))).Error,
            (await AsAsync<ScoringRuleAdministrationService, Result<int>>(user, s => s.PublishAsync([.. rules.Skip(1)], Ct))).Error,
            (await AsAsync<ApprovalThresholdsAdministrationService, Result>(user, s => s.ChangeAsync(1m, 2m, 2, Ct))).Error,
            (await AsAsync<FxRateAdministrationService, Result>(user, s => s.SetAsync(Currency.USD, day, 9m, Ct))).Error,
            (await AsAsync<FxRateAdministrationService, Result<int>>(user, s => s.ImportAsync("USD,2026-12-30,9", Ct))).Error,
            (await AsAsync<HolidayAdministrationService, Result>(user, s => s.AddAsync(day, "No", Ct))).Error,
        };

        Assert.All(results, error => Assert.Equal(CommonErrors.AccessDenied, error));
        Assert.Equal(2, await Host.QueryAsync(db => db.LoanProducts.Where(p => p.Id == product.Id).SelectMany(p => p.Versions).CountAsync(Ct)));
        Assert.Equal(1, await Host.QueryAsync(db => db.ScoringRuleSets.CountAsync(Ct)));
        Assert.Null(await RateAsync(Currency.USD, day));
    }

    // ----- Helpers -----

    private Task<TResult> AsAsync<TService, TResult>(UserId user, Func<TService, Task<TResult>> call)
        where TService : notnull =>
        Host.InScopeAsAsync(user, sp => call(sp.GetRequiredService<TService>()));

    private Task<UserId> UserAsync(string userName) =>
        Host.QueryAsync(async db => new UserId(
            await db.UserDirectory.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(Ct)));

    private Task<decimal?> RateAsync(Currency currency, DateOnly day) =>
        Host.QueryAsync(db => db.FxRates.Where(r => r.Currency == currency && r.EffectiveDate == day).Select(r => (decimal?)r.GelPerUnit).SingleOrDefaultAsync(Ct));

    /// <summary>A data-audit record of <paramref name="entityType"/> was written after the test started.</summary>
    private Task<bool> AuditedAsync(string entityType) =>
        Host.QueryAsync(db => db.AuditLogs.AnyAsync(a => a.EntityType == entityType && a.OccurredAt >= _clock.GetUtcNow().AddMinutes(-1), Ct));
}
