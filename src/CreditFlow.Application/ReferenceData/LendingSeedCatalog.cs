using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;

namespace CreditFlow.Application.ReferenceData;

/// <summary>A seeded product: code, name, type and its versions in order (the last one is current).</summary>
public sealed record ProductSeed(string Code, string Name, ProductType Type, IReadOnlyList<ProductTerms> Versions);

/// <summary>
/// The lending reference data seeded in every environment: the three products of the task (the consumer product
/// with two versions to show versioning), FX rates and the Georgian public holidays used by the working-day calendar.
/// </summary>
public static class LendingSeedCatalog
{
    public const string Consumer = "CONS";
    public const string Mortgage = "MORT";
    public const string Auto = "AUTO";

    private static readonly ProductTerms ConsumerV1 = new()
    {
        MinAmountGel = 500m,
        MaxAmountGel = 50_000m,
        MinTermMonths = 6,
        MaxTermMonths = 60,
        MinRate = 14m,
        MaxRate = 26m,
        UpfrontFeePercent = 1m,
        MonthlyFeePercent = 0m,
        EarlyRepaymentPenaltyPercent = 2m,
        Currencies = [Currency.GEL],
        ScheduleTypes = [ScheduleType.Annuity, ScheduleType.Differentiated],
        RequiredDocuments =
        [
            new(DocumentType.IdentityDocumentCopy),
            new(DocumentType.IncomeStatement),
            new(DocumentType.EmploymentCertificate, MinAmountGel: 20_000m),
        ],
    };

    public static IReadOnlyList<ProductSeed> Products { get; } =
    [
        new(Consumer, "Consumer loan", ProductType.Consumer,
        [
            ConsumerV1,
            ConsumerV1 with { MaxRate = 24m, UpfrontFeePercent = 0.5m },
        ]),
        new(Mortgage, "Mortgage", ProductType.Mortgage,
        [
            new()
            {
                MinAmountGel = 20_000m,
                MaxAmountGel = 1_500_000m,
                MinTermMonths = 36,
                MaxTermMonths = 300,
                MinRate = 8m,
                MaxRate = 14m,
                UpfrontFeePercent = 0.5m,
                EarlyRepaymentPenaltyPercent = 1m,
                CollateralRequired = true,
                MaxLtvPercent = 80m,
                Currencies = [Currency.GEL, Currency.USD, Currency.EUR],
                ScheduleTypes = [ScheduleType.Annuity, ScheduleType.Differentiated],
                RequiredDocuments =
                [
                    new(DocumentType.IdentityDocumentCopy),
                    new(DocumentType.IncomeStatement),
                    new(DocumentType.PropertyValuationReport),
                    new(DocumentType.CadastralExtract),
                    new(DocumentType.BankStatement, MinAmountGel: 300_000m),
                ],
                StandardConditions = [ConditionType.CollateralRegistration, ConditionType.CollateralInsurance],
            },
        ]),
        new(Auto, "Car loan", ProductType.Auto,
        [
            new()
            {
                MinAmountGel = 5_000m,
                MaxAmountGel = 150_000m,
                MinTermMonths = 12,
                MaxTermMonths = 84,
                MinRate = 11m,
                MaxRate = 18m,
                UpfrontFeePercent = 1m,
                MonthlyFeePercent = 0.1m,
                EarlyRepaymentPenaltyPercent = 1m,
                CollateralRequired = true,
                MaxLtvPercent = 70m,
                Currencies = [Currency.GEL, Currency.USD],
                ScheduleTypes = [ScheduleType.Annuity],
                RequiredDocuments =
                [
                    new(DocumentType.IdentityDocumentCopy),
                    new(DocumentType.IncomeStatement),
                    new(DocumentType.VehicleValuationReport),
                    new(DocumentType.VehicleRegistration),
                ],
                StandardConditions = [ConditionType.CollateralRegistration, ConditionType.CollateralInsurance],
            },
        ]),
    ];

    /// <summary>GEL per unit, from the given date (illustrative official rates).</summary>
    public static IReadOnlyList<(Currency Currency, DateOnly EffectiveDate, decimal GelPerUnit)> FxRates { get; } =
    [
        (Currency.USD, new DateOnly(2026, 1, 1), 2.6900m),
        (Currency.EUR, new DateOnly(2026, 1, 1), 2.9300m),
        (Currency.USD, new DateOnly(2026, 10, 1), 2.7134m),
        (Currency.EUR, new DateOnly(2026, 10, 1), 3.1652m),
    ];

    /// <summary>Georgian public holidays (Labour Code, art. 30), including Orthodox Easter.</summary>
    public static IReadOnlyList<(DateOnly Date, string Name)> Holidays { get; } =
    [
        .. YearHolidays(2026, orthodoxEaster: new DateOnly(2026, 4, 12)),
        .. YearHolidays(2027, orthodoxEaster: new DateOnly(2027, 5, 2)),
    ];

    private static IEnumerable<(DateOnly, string)> YearHolidays(int year, DateOnly orthodoxEaster) =>
    [
        (new DateOnly(year, 1, 1), "New Year"),
        (new DateOnly(year, 1, 2), "New Year"),
        (new DateOnly(year, 1, 7), "Orthodox Christmas"),
        (new DateOnly(year, 1, 19), "Epiphany"),
        (new DateOnly(year, 3, 3), "Mother's Day"),
        (new DateOnly(year, 3, 8), "International Women's Day"),
        (new DateOnly(year, 4, 9), "National Unity Day"),
        (orthodoxEaster.AddDays(-2), "Good Friday"),
        (orthodoxEaster.AddDays(-1), "Holy Saturday"),
        (orthodoxEaster, "Easter Sunday"),
        (orthodoxEaster.AddDays(1), "Easter Monday"),
        (new DateOnly(year, 5, 9), "Victory Day"),
        (new DateOnly(year, 5, 12), "Saint Andrew's Day"),
        (new DateOnly(year, 5, 26), "Independence Day"),
        (new DateOnly(year, 8, 28), "Saint Mary's Day"),
        (new DateOnly(year, 10, 14), "Svetitskhovloba"),
        (new DateOnly(year, 11, 23), "Saint George's Day"),
    ];
}
