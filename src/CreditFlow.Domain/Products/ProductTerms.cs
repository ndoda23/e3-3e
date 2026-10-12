using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Products;

public enum ProductType
{
    Consumer = 1,
    Mortgage = 2,
    Auto = 3,
}

public enum ScheduleType
{
    Annuity = 1,
    Differentiated = 2,
}

/// <summary>Documents an application may need (the checklist depends on the product and the amount).</summary>
public enum DocumentType
{
    IdentityDocumentCopy = 1,
    IncomeStatement = 2,
    EmploymentCertificate = 3,
    BankStatement = 4,
    PropertyValuationReport = 5,
    CadastralExtract = 6,
    VehicleValuationReport = 7,
    VehicleRegistration = 8,
    Other = 9,
}

/// <summary>Conditions to meet between contract signing and disbursement.</summary>
public enum ConditionType
{
    CollateralInsurance = 1,
    CollateralRegistration = 2,
    LifeInsurance = 3,
    AdditionalDocument = 4,
    Other = 5,
}

/// <summary>A required document; with <see cref="MinAmountGel"/> it is required only from that amount (GEL equivalent).</summary>
public sealed record DocumentRequirement(DocumentType Type, decimal? MinAmountGel = null)
{
    public bool AppliesTo(decimal amountGel) => MinAmountGel is not { } minimum || amountGel >= minimum;
}

/// <summary>
/// The parameters of one product version. Amount limits are in GEL; a loan in USD or EUR is checked by its GEL
/// equivalent at the rate of the submission date. Rates and fees are percentages (annual for the interest rate).
/// The snapshot stored on an application at submission is a copy of these terms.
/// </summary>
public sealed record ProductTerms
{
    /// <summary>Regulatory LTV ceilings per product type, in percent (the product may be stricter).</summary>
    public static readonly IReadOnlyDictionary<ProductType, decimal> LtvCeiling = new Dictionary<ProductType, decimal>
    {
        [ProductType.Mortgage] = 80m,
        [ProductType.Auto] = 70m,
    };

    public const int MaxTermMonthsLimit = 360;
    public const decimal MaxPercent = 100m;

    public required decimal MinAmountGel { get; init; }

    public required decimal MaxAmountGel { get; init; }

    public required int MinTermMonths { get; init; }

    public required int MaxTermMonths { get; init; }

    /// <summary>Lowest annual interest rate, percent.</summary>
    public required decimal MinRate { get; init; }

    /// <summary>Highest annual interest rate, percent.</summary>
    public required decimal MaxRate { get; init; }

    /// <summary>One-off fee deducted at disbursement, percent of the principal.</summary>
    public decimal UpfrontFeePercent { get; init; }

    /// <summary>Monthly service fee, percent of the original principal per month.</summary>
    public decimal MonthlyFeePercent { get; init; }

    /// <summary>Penalty on early repayment, percent of the prepaid principal.</summary>
    public decimal EarlyRepaymentPenaltyPercent { get; init; }

    public bool CollateralRequired { get; init; }

    /// <summary>Highest loan-to-value, percent; <c>null</c> when the product takes no collateral.</summary>
    public decimal? MaxLtvPercent { get; init; }

    public required IReadOnlyList<Currency> Currencies { get; init; }

    public required IReadOnlyList<ScheduleType> ScheduleTypes { get; init; }

    public IReadOnlyList<DocumentRequirement> RequiredDocuments { get; init; } = [];

    /// <summary>Conditions added to every approval of this product (e.g. collateral registration and insurance).</summary>
    public IReadOnlyList<ConditionType> StandardConditions { get; init; } = [];

    /// <summary>Every rule broken by these terms for the given product type (empty when valid).</summary>
    public IReadOnlyList<Error> Validate(ProductType type)
    {
        var errors = new List<Error>();

        if (MinAmountGel <= 0m || MaxAmountGel < MinAmountGel)
        {
            errors.Add(ProductErrors.InvalidAmountRange);
        }

        if (MinTermMonths < 1 || MaxTermMonths < MinTermMonths || MaxTermMonths > MaxTermMonthsLimit)
        {
            errors.Add(ProductErrors.InvalidTermRange);
        }

        if (MinRate < 0m || MaxRate < MinRate || MaxRate > MaxPercent)
        {
            errors.Add(ProductErrors.InvalidRateRange);
        }

        if (!IsPercent(UpfrontFeePercent) || !IsPercent(MonthlyFeePercent) || !IsPercent(EarlyRepaymentPenaltyPercent))
        {
            errors.Add(ProductErrors.InvalidFee);
        }

        if (Currencies.Count == 0 || Currencies.Any(c => !Enum.IsDefined(c)) || Currencies.Distinct().Count() != Currencies.Count)
        {
            errors.Add(ProductErrors.InvalidCurrencies);
        }

        if (ScheduleTypes.Count == 0 || ScheduleTypes.Any(s => !Enum.IsDefined(s)) || ScheduleTypes.Distinct().Count() != ScheduleTypes.Count)
        {
            errors.Add(ProductErrors.InvalidScheduleTypes);
        }

        if (RequiredDocuments.Any(d => !Enum.IsDefined(d.Type) || d.MinAmountGel is < 0m)
            || StandardConditions.Any(c => !Enum.IsDefined(c)))
        {
            errors.Add(ProductErrors.InvalidChecklist);
        }

        errors.AddRange(ValidateCollateral(type));
        return errors;
    }

    /// <summary>Value equality including the contents of the lists (records compare lists by reference).</summary>
    public bool Equals(ProductTerms? other) =>
        other is not null
        && MinAmountGel == other.MinAmountGel && MaxAmountGel == other.MaxAmountGel
        && MinTermMonths == other.MinTermMonths && MaxTermMonths == other.MaxTermMonths
        && MinRate == other.MinRate && MaxRate == other.MaxRate
        && UpfrontFeePercent == other.UpfrontFeePercent && MonthlyFeePercent == other.MonthlyFeePercent
        && EarlyRepaymentPenaltyPercent == other.EarlyRepaymentPenaltyPercent
        && CollateralRequired == other.CollateralRequired && MaxLtvPercent == other.MaxLtvPercent
        && Currencies.SequenceEqual(other.Currencies)
        && ScheduleTypes.SequenceEqual(other.ScheduleTypes)
        && RequiredDocuments.SequenceEqual(other.RequiredDocuments)
        && StandardConditions.SequenceEqual(other.StandardConditions);

    public override int GetHashCode() =>
        HashCode.Combine(MinAmountGel, MaxAmountGel, MinTermMonths, MaxTermMonths, MinRate, MaxRate, Currencies.Count);

    public bool AllowsAmount(decimal amountGel) => amountGel >= MinAmountGel && amountGel <= MaxAmountGel;

    public bool AllowsTerm(int months) => months >= MinTermMonths && months <= MaxTermMonths;

    public bool AllowsRate(decimal rate) => rate >= MinRate && rate <= MaxRate;

    /// <summary>The documents required for an application of this amount (GEL equivalent).</summary>
    public IReadOnlyList<DocumentType> DocumentsRequiredFor(decimal amountGel) =>
        RequiredDocuments.Where(d => d.AppliesTo(amountGel)).Select(d => d.Type).Distinct().ToList();

    private IEnumerable<Error> ValidateCollateral(ProductType type)
    {
        if (LtvCeiling.TryGetValue(type, out var ceiling))
        {
            // Secured products: collateral is mandatory and the LTV may not exceed the regulatory ceiling.
            if (!CollateralRequired || MaxLtvPercent is not { } ltv || ltv <= 0m || ltv > ceiling)
            {
                yield return ProductErrors.InvalidLtv(type, ceiling);
            }
        }
        else if (CollateralRequired || MaxLtvPercent is not null)
        {
            yield return ProductErrors.UnsecuredProductHasNoCollateral;
        }
    }

    private static bool IsPercent(decimal value) => value is >= 0m and <= MaxPercent;
}
