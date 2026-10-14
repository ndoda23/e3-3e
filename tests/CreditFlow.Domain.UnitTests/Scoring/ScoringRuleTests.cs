using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.UnitTests.Applications;

namespace CreditFlow.Domain.UnitTests.Scoring;

public sealed class ScoringRuleTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    private const string PtiBands =
        """{"bands":[{"upToIncomeGel":1000,"maxPtiPercent":35},{"upToIncomeGel":3000,"maxPtiPercent":45},{"upToIncomeGel":null,"maxPtiPercent":55}],"fxStressPercent":20}""";

    private static readonly Dictionary<Currency, ExchangeRate> Rates = new()
    {
        [Currency.GEL] = ExchangeRate.Gel(Today),
        [Currency.USD] = new(Currency.USD, 2.5m, Today),
        [Currency.EUR] = new(Currency.EUR, 3m, Today),
    };

    private static Money Gel(decimal amount) => Money.Of(amount, Currency.GEL);

    private static PartyFacts Party(
        PartyRole role = PartyRole.Borrower,
        decimal incomeGel = 2_000m,
        decimal obligationsGel = 0m,
        IReadOnlyList<Delinquency>? delinquencies = null,
        decimal exposureGel = 0m,
        IReadOnlyList<Money>? incomes = null,
        string name = "Anna") =>
        new(ClientId.New(), name, role, incomes ?? [Gel(incomeGel)], obligationsGel > 0 ? [Gel(obligationsGel)] : [], 0m,
            new BureauReport(delinquencies ?? [], []), exposureGel);

    private static ScoringInput Input(
        IReadOnlyList<PartyFacts>? parties = null,
        Money? installment = null,
        decimal amountGel = 10_000m,
        Currency currency = Currency.GEL,
        ProductTerms? product = null,
        IReadOnlyList<Collateral>? collaterals = null) =>
        new(
            LoanTerms.Create(Money.Of(amountGel / Rates[currency].GelPerUnit, currency), 24, 18m, ScheduleType.Annuity).Value,
            product ?? ApplicationFixture.ConsumerTerms(),
            amountGel,
            installment ?? Money.Of(500m, currency),
            collaterals ?? [],
            parties ?? [Party()],
            Rates,
            Today);

    private static RuleOutcome Run(IScoringRule rule, ScoringInput input, string parameters = "{}") => rule.Evaluate(input, parameters);

    // ----- PTI -----

    [Theory]
    [InlineData(999.99, 349.99, true)]  // band < 1,000: 35 %
    [InlineData(999.99, 350.00, false)]
    [InlineData(1000, 450, true)]        // band 1,000–3,000: 45 %
    [InlineData(1000, 450.01, false)]
    [InlineData(3000, 1650, true)]       // band ≥ 3,000: 55 %
    [InlineData(3000, 1650.01, false)]
    public void PTI_limits_depend_on_the_income_band(double income, double installment, bool passes)
    {
        var input = Input([Party(incomeGel: (decimal)income)], Gel((decimal)installment));

        Assert.Equal(passes, Run(new PtiRule(), input, PtiBands).Passed);
    }

    [Fact]
    public void PTI_counts_every_obligation_and_the_new_installment()
    {
        // (600 + 300) / 2,000 = 45 % ≤ 45 %; one more lari fails.
        Assert.True(Run(new PtiRule(), Input([Party(incomeGel: 2_000m, obligationsGel: 600m)], Gel(300m)), PtiBands).Passed);
        var failed = Run(new PtiRule(), Input([Party(incomeGel: 2_000m, obligationsGel: 600m)], Gel(301m)), PtiBands);

        Assert.False(failed.Passed);
        Assert.Equal("45.05 %", failed.Measured);
        Assert.Contains("for income 2,000.00 GEL", failed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_foreign_currency_installment_is_stressed_by_20_percent()
    {
        // 360 USD = 900 GEL, stressed 1,080 GEL. Income 2,400 GEL: 45 % passes; without the stress it would be 37.5 %.
        var passes = Input([Party(incomeGel: 2_400m)], Money.Of(360m, Currency.USD), currency: Currency.USD);
        var fails = Input([Party(incomeGel: 2_399m)], Money.Of(360m, Currency.USD), currency: Currency.USD);

        Assert.True(Run(new PtiRule(), passes, PtiBands).Passed);
        Assert.False(Run(new PtiRule(), fails, PtiBands).Passed);
        Assert.Contains("stressed", Run(new PtiRule(), fails, PtiBands).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Household_incomes_are_combined_and_guarantors_are_tested_alone()
    {
        var household = Input([Party(incomeGel: 600m), Party(PartyRole.CoBorrower, incomeGel: 600m)], Gel(500m));
        Assert.True(Run(new PtiRule(), household, PtiBands).Passed); // 500 / 1,200 = 41.67 % ≤ 45 %

        var weakGuarantor = Input([Party(incomeGel: 5_000m), Party(PartyRole.Guarantor, incomeGel: 1_000m, name: "Giorgi")], Gel(500m));
        var outcome = Run(new PtiRule(), weakGuarantor, PtiBands);
        Assert.False(outcome.Passed); // guarantor: 500 / 1,000 = 50 % > 45 %
        Assert.Contains("Guarantor Giorgi", outcome.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Incomes_in_other_currencies_are_converted()
    {
        var input = Input([Party(incomes: [Money.Of(400m, Currency.USD)])], Gel(450m)); // 1,000 GEL → 45 %

        Assert.True(Run(new PtiRule(), input, PtiBands).Passed);
    }

    [Fact]
    public void No_income_fails_PTI()
    {
        Assert.False(Run(new PtiRule(), Input([Party(incomes: [])]), PtiBands).Passed);
    }

    // ----- Bureau -----

    [Theory]
    [InlineData(29, 0, true)]
    [InlineData(30, 0, false)]
    [InlineData(90, 13, true)]   // older than 12 months
    [InlineData(30, 12, false)]  // exactly 12 months ago still counts
    public void Thirty_days_past_due_in_the_last_twelve_months_fails(int days, int monthsAgo, bool passes)
    {
        var input = Input([Party(delinquencies: [new Delinquency(Today.AddMonths(-monthsAgo), days)])]);

        Assert.Equal(passes, Run(new BureauDelinquencyRule(), input, """{"minDaysPastDue":30,"lookbackMonths":12}""").Passed);
    }

    [Fact]
    public void A_co_borrowers_bad_history_also_fails_and_a_missing_report_fails()
    {
        var coBorrower = Party(PartyRole.CoBorrower, delinquencies: [new Delinquency(Today.AddDays(-5), 45)], name: "Nino");
        var outcome = Run(new BureauDelinquencyRule(), Input([Party(), coBorrower]), """{"minDaysPastDue":30,"lookbackMonths":12}""");
        Assert.False(outcome.Passed);
        Assert.Contains("Nino", outcome.Reason, StringComparison.Ordinal);

        var noReport = Party() with { Bureau = null };
        Assert.False(Run(new BureauDelinquencyRule(), Input([noReport]), """{"minDaysPastDue":30,"lookbackMonths":12}""").Passed);
    }

    // ----- LTV and valuation -----

    private static readonly ProductTerms Mortgage =
        ApplicationFixture.ConsumerTerms() with { CollateralRequired = true, MaxLtvPercent = 80m };

    private static Collateral Flat(decimal value, DateOnly valuedOn)
    {
        var application = new ApplicationFixture().Draft();
        var id = application.AddCollateral(CollateralType.RealEstate, "Flat", Gel(value), valuedOn, Today).Value;
        return application.Collaterals.Single(c => c.Id == id);
    }

    [Theory]
    [InlineData(80_000, true)]
    [InlineData(80_001, false)]
    public void LTV_of_a_mortgage_is_at_most_80_percent(double amountGel, bool passes)
    {
        var input = Input(amountGel: (decimal)amountGel, product: Mortgage, collaterals: [Flat(100_000m, Today)]);

        Assert.Equal(passes, Run(new LtvRule(), input).Passed);
    }

    [Fact]
    public void LTV_does_not_apply_to_unsecured_products_and_fails_without_collateral()
    {
        Assert.True(Run(new LtvRule(), Input()).Passed);
        Assert.False(Run(new LtvRule(), Input(product: Mortgage)).Passed);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(6, true)]  // exactly six months old
    [InlineData(7, false)]
    public void Valuations_are_at_most_six_months_old(int monthsAgo, bool passes)
    {
        var valuedOn = monthsAgo == 7 ? Today.AddMonths(-6).AddDays(-1) : Today.AddMonths(-monthsAgo);
        var input = Input(product: Mortgage, collaterals: [Flat(100_000m, valuedOn)]);

        Assert.Equal(passes, Run(new ValuationAgeRule(), input, """{"maxAgeMonths":6}""").Passed);
    }

    // ----- FX income -----

    [Theory]
    [InlineData(199_999.99, false, false)]
    [InlineData(199_999.99, true, true)]
    [InlineData(200_000, false, true)]
    public void A_foreign_currency_loan_below_the_threshold_needs_income_in_that_currency(double amountGel, bool usdIncome, bool passes)
    {
        var incomes = usdIncome ? new[] { Money.Of(1_000m, Currency.USD) } : [Gel(5_000m)];
        var input = Input([Party(incomes: incomes)], amountGel: (decimal)amountGel, currency: Currency.USD);

        Assert.Equal(passes, Run(new FxIncomeCurrencyRule(), input, """{"thresholdGel":200000}""").Passed);
    }

    [Fact]
    public void GEL_loans_need_no_foreign_income()
    {
        Assert.True(Run(new FxIncomeCurrencyRule(), Input(), """{"thresholdGel":200000}""").Passed);
    }

    // ----- Exposure -----

    [Theory]
    [InlineData(290_000, true)]
    [InlineData(290_000.01, false)]
    public void Exposure_with_the_new_loan_is_at_most_the_limit(double existing, bool passes)
    {
        var input = Input([Party(exposureGel: (decimal)existing)]);

        Assert.Equal(passes, Run(new ExposureLimitRule(), input, """{"limitGel":300000}""").Passed);
    }

    [Fact]
    public void A_guarantor_counts_the_guaranteed_amount_in_their_exposure()
    {
        var input = Input([Party(), Party(PartyRole.Guarantor, exposureGel: 295_000m, name: "Levan")]);

        var outcome = Run(new ExposureLimitRule(), input, """{"limitGel":300000}""");

        Assert.False(outcome.Passed);
        Assert.Contains("Levan", outcome.Reason, StringComparison.Ordinal);
    }

    // ----- Product limits -----

    [Fact]
    public void Product_limits_are_checked_again_at_scoring()
    {
        Assert.True(Run(new ProductLimitsRule(), Input()).Passed);
        Assert.False(Run(new ProductLimitsRule(), Input(amountGel: 200_000.01m)).Passed);
    }

    // ----- Engine and rule sets -----

    private static readonly IReadOnlyList<RuleDefinition> Rules =
    [
        new("PRODUCT_LIMITS", false, "{}"),
        new("BUREAU_DPD30_12M", false, """{"minDaysPastDue":30,"lookbackMonths":12}"""),
        new("PTI", true, PtiBands),
        new("EXPOSURE_LIMIT", false, """{"limitGel":300000}"""),
    ];

    [Fact]
    public void A_run_keeps_every_rule_with_the_rule_set_version_and_the_overridable_flag()
    {
        var engine = ScoringEngine.Standard();
        var set = ScoringRuleSet.Create(3, Rules, engine, DateTimeOffset.UtcNow).Value;

        var run = engine.Score(set, Input([Party(incomeGel: 1_000m)], Gel(500m)), DateTimeOffset.UtcNow);

        Assert.Equal(3, run.RuleSetVersion);
        Assert.Equal(["PRODUCT_LIMITS", "BUREAU_DPD30_12M", "PTI", "EXPOSURE_LIMIT"], run.Results.Select(r => r.RuleCode));
        var pti = run.Results.Single(r => r.RuleCode == "PTI");
        Assert.False(pti.Passed);
        Assert.True(pti.Overridable);
        Assert.False(run.HasHardFailure);
        Assert.True(run.RequiresOverride);
    }

    [Fact]
    public void A_rule_set_rejects_unknown_rules_bad_parameters_and_duplicates()
    {
        var engine = ScoringEngine.Standard();

        var result = ScoringRuleSet.Create(1,
        [
            new("NO_SUCH_RULE", false, "{}"),
            new("PTI", true, """{"bands":[{"upToIncomeGel":1000,"maxPtiPercent":35}],"fxStressPercent":20}"""),
            new("EXPOSURE_LIMIT", false, "not json"),
        ], engine, DateTimeOffset.UtcNow);

        Assert.Equal(["Scoring.UnknownRule", "Scoring.InvalidParameters", "Scoring.InvalidParameters"],
            result.Error!.Details.Select(e => e.Code));
        Assert.Equal(ScoringErrors.RulesMustBeUnique,
            ScoringRuleSet.Create(1, [Rules[0], Rules[0]], engine, DateTimeOffset.UtcNow).Error);
    }

    [Fact]
    public void Publishing_the_next_version_increments_it()
    {
        var engine = ScoringEngine.Standard();
        var v1 = ScoringRuleSet.Create(1, Rules, engine, DateTimeOffset.UtcNow).Value;

        Assert.Equal(2, v1.PublishNext(Rules.Take(2).ToList(), engine, DateTimeOffset.UtcNow).Value.Version);
    }

    // ----- Approval thresholds -----

    [Theory]
    [InlineData(20_000, ApprovalTier.Underwriter)]
    [InlineData(20_000.01, ApprovalTier.SeniorUnderwriter)]
    [InlineData(100_000, ApprovalTier.SeniorUnderwriter)]
    [InlineData(100_000.01, ApprovalTier.Committee)]
    public void The_approval_tier_follows_the_thresholds(double amountGel, ApprovalTier tier)
    {
        Assert.Equal(tier, ApprovalThresholds.Default().TierFor((decimal)amountGel));
    }

    [Fact]
    public void Thresholds_are_configurable_and_validated()
    {
        var thresholds = ApprovalThresholds.Default();

        Assert.True(thresholds.Change(30_000m, 150_000m, 3).IsSuccess);
        Assert.Equal(ApprovalTier.Underwriter, thresholds.TierFor(30_000m));
        Assert.Equal(
            ["Approval.InvalidThresholds", "Approval.InvalidRequiredVotes"],
            thresholds.Change(150_000m, 150_000m, 0).Error!.Details.Select(e => e.Code));
        Assert.Equal(2, ApprovalThresholds.Default().RequiredVotes);
    }
}
