using CreditFlow.Domain.Scoring;

namespace CreditFlow.Application.ReferenceData;

/// <summary>
/// Version 1 of the scoring rules (seeded; the Admin publishes later versions). The values are the approved
/// decisions in PLAN.md §P.4: PTI 35 / 45 / 55 % by income band with +20 % FX stress (the only overridable rule),
/// LTV from the product, valuations at most 6 months old, no 30+ days past due in 12 months, FX loans below
/// 200,000 GEL only with income in the loan currency, exposure at most 300,000 GEL per client.
/// </summary>
public static class ScoringRuleSetSeed
{
    public static IReadOnlyList<RuleDefinition> Version1 { get; } =
    [
        new("PRODUCT_LIMITS", Overridable: false, "{}"),
        new("BUREAU_DPD30_12M", Overridable: false, """{"minDaysPastDue":30,"lookbackMonths":12}"""),
        new("PTI", Overridable: true,
            """{"bands":[{"upToIncomeGel":1000,"maxPtiPercent":35},{"upToIncomeGel":3000,"maxPtiPercent":45},{"upToIncomeGel":null,"maxPtiPercent":55}],"fxStressPercent":20}"""),
        new("LTV", Overridable: false, "{}"),
        new("VALUATION_AGE", Overridable: false, """{"maxAgeMonths":6}"""),
        new("FX_INCOME_CURRENCY", Overridable: false, """{"thresholdGel":200000}"""),
        new("EXPOSURE_LIMIT", Overridable: false, """{"limitGel":300000}"""),
    ];
}
