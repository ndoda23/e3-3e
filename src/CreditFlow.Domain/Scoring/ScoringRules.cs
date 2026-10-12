using System.Globalization;
using System.Text.Json;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Scoring;

/// <summary>A rule whose parameters are a JSON object of type <typeparamref name="TParameters"/>.</summary>
public abstract class ScoringRule<TParameters> : IScoringRule
    where TParameters : class
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public abstract string Code { get; }

    public string? ValidateParameters(string parametersJson)
    {
        try
        {
            return JsonSerializer.Deserialize<TParameters>(parametersJson, Json) is { } parameters
                ? Validate(parameters)
                : "parameters are missing.";
        }
        catch (JsonException exception)
        {
            return exception.Message;
        }
    }

    public RuleOutcome Evaluate(ScoringInput input, string parametersJson)
    {
        ArgumentNullException.ThrowIfNull(input);
        var parameters = JsonSerializer.Deserialize<TParameters>(parametersJson, Json)
                         ?? throw new InvalidOperationException($"{Code}: parameters are missing.");
        return Evaluate(input, parameters);
    }

    /// <summary>A description of the problem, or <c>null</c> when the parameters are valid.</summary>
    protected abstract string? Validate(TParameters parameters);

    protected abstract RuleOutcome Evaluate(ScoringInput input, TParameters parameters);

    protected static string Number(decimal value) => value.ToString("#,0.00", CultureInfo.InvariantCulture);

    /// <summary>A percentage for display: rounded up (towards the limit) so that a failing value never prints as equal to it.</summary>
    protected static string Percent(decimal value) =>
        (Math.Ceiling(value * 100m) / 100m).ToString("0.00", CultureInfo.InvariantCulture) + " %";
}

/// <summary>A rule without parameters (<c>{}</c>).</summary>
public sealed record NoParameters;

/// <summary><c>PRODUCT_LIMITS</c>: amount (GEL equivalent), term, rate and currency within the product snapshot.</summary>
public sealed class ProductLimitsRule : ScoringRule<NoParameters>
{
    public override string Code => "PRODUCT_LIMITS";

    protected override string? Validate(NoParameters parameters) => null;

    protected override RuleOutcome Evaluate(ScoringInput input, NoParameters parameters)
    {
        var product = input.Product;
        var problems = new List<string>();
        if (!product.AllowsAmount(input.AmountGel))
        {
            problems.Add($"amount {Number(input.AmountGel)} GEL outside {Number(product.MinAmountGel)}–{Number(product.MaxAmountGel)} GEL");
        }

        if (!product.AllowsTerm(input.Terms.TermMonths))
        {
            problems.Add($"term {input.Terms.TermMonths} months outside {product.MinTermMonths}–{product.MaxTermMonths}");
        }

        if (!product.AllowsRate(input.Terms.AnnualRate))
        {
            problems.Add($"rate {Percent(input.Terms.AnnualRate)} outside {Percent(product.MinRate)}–{Percent(product.MaxRate)}");
        }

        if (!product.Currencies.Contains(input.Terms.Amount.Currency))
        {
            problems.Add($"currency {input.Terms.Amount.Currency} not offered");
        }

        return problems.Count == 0
            ? new RuleOutcome(true, null, null, "Amount, term, rate and currency are within the product.")
            : new RuleOutcome(false, null, null, "Outside the product: " + string.Join("; ", problems) + ".");
    }
}

/// <param name="MinDaysPastDue">A delinquency of at least this many days fails the rule (30).</param>
/// <param name="LookbackMonths">How far back delinquencies count (12).</param>
public sealed record DelinquencyParameters(int MinDaysPastDue, int LookbackMonths);

/// <summary><c>BUREAU_DPD30_12M</c>: no party was 30+ days past due in the last 12 months.</summary>
public sealed class BureauDelinquencyRule : ScoringRule<DelinquencyParameters>
{
    public override string Code => "BUREAU_DPD30_12M";

    protected override string? Validate(DelinquencyParameters parameters) =>
        parameters.MinDaysPastDue < 1 || parameters.LookbackMonths < 1 ? "days and months must be positive." : null;

    protected override RuleOutcome Evaluate(ScoringInput input, DelinquencyParameters parameters)
    {
        var since = input.Today.AddMonths(-parameters.LookbackMonths);
        var threshold = $"< {parameters.MinDaysPastDue} days in {parameters.LookbackMonths} months";

        var missing = input.Parties.Where(p => p.Bureau is null).Select(p => p.Name).ToList();
        if (missing.Count > 0)
        {
            return new RuleOutcome(false, null, threshold, "No bureau report for " + string.Join(", ", missing) + ".");
        }

        var worst = input.Parties
            .SelectMany(p => p.Bureau!.Delinquencies.Where(d => d.Date >= since).Select(d => (p.Name, d.DaysPastDue)))
            .OrderByDescending(x => x.DaysPastDue)
            .FirstOrDefault();

        return worst.DaysPastDue >= parameters.MinDaysPastDue
            ? new RuleOutcome(false, $"{worst.DaysPastDue} days", threshold,
                $"{worst.Name} was {worst.DaysPastDue} days past due within the last {parameters.LookbackMonths} months.")
            : new RuleOutcome(true, $"{worst.DaysPastDue} days", threshold, "No serious late payment in the bureau.");
    }
}

/// <param name="UpToIncomeGel">The band applies to monthly incomes below this (GEL); <c>null</c> for the top band.</param>
/// <param name="MaxPtiPercent">The highest PTI allowed in the band.</param>
public sealed record PtiBand(decimal? UpToIncomeGel, decimal MaxPtiPercent);

/// <param name="Bands">Income bands, ascending, the last without an upper bound.</param>
/// <param name="FxStressPercent">Added to the new installment of a USD or EUR loan (20).</param>
public sealed record PtiParameters(IReadOnlyList<PtiBand> Bands, decimal FxStressPercent);

/// <summary>
/// <c>PTI</c>: monthly obligations including the new installment, divided by monthly income, within the band of the
/// income. The household (borrower and co-borrowers) is tested together, every guarantor alone with the full new
/// installment. For a foreign-currency loan the installment is stressed (+20 %).
/// </summary>
public sealed class PtiRule : ScoringRule<PtiParameters>
{
    public override string Code => "PTI";

    protected override string? Validate(PtiParameters parameters)
    {
        var bands = parameters.Bands ?? [];
        if (bands.Count == 0 || bands[^1].UpToIncomeGel is not null)
        {
            return "the last band must have no upper bound.";
        }

        var bounds = bands.Take(bands.Count - 1).Select(b => b.UpToIncomeGel).ToList();
        if (bounds.Any(b => b is null or <= 0m) || !bounds.SequenceEqual(bounds.Order()) || bounds.Distinct().Count() != bounds.Count)
        {
            return "band bounds must be positive and ascending.";
        }

        return bands.Any(b => b.MaxPtiPercent is <= 0m or > 100m) || parameters.FxStressPercent is < 0m or > 100m
            ? "percentages must be 0 to 100."
            : null;
    }

    protected override RuleOutcome Evaluate(ScoringInput input, PtiParameters parameters)
    {
        var stress = input.Terms.Amount.Currency == Currency.GEL ? 1m : 1m + (parameters.FxStressPercent / 100m);
        var installmentGel = Money.Round(input.ToGel(input.NewInstallment) * stress);

        var household = input.Parties.Where(p => p.Role is PartyRole.Borrower or PartyRole.CoBorrower).ToList();
        var tests = new List<(string Who, decimal Income, decimal Obligations)>
        {
            ("Household", household.Sum(p => Income(input, p)), household.Sum(p => Obligations(input, p))),
        };
        tests.AddRange(input.Parties.Where(p => p.Role == PartyRole.Guarantor)
            .Select(p => ($"Guarantor {p.Name}", Income(input, p), Obligations(input, p))));

        var failures = new List<string>();
        string? measured = null, threshold = null;
        foreach (var (who, income, obligations) in tests)
        {
            var limit = LimitFor(parameters.Bands, income);
            // Compared exactly; rounded only for display (rounding first would let 45.004 % pass a 45 % limit).
            var pti = income > 0m ? (obligations + installmentGel) / income * 100m : decimal.MaxValue;
            measured ??= income > 0m ? Percent(pti) : "no income";
            threshold ??= $"≤ {Percent(limit)} for income {Number(income)} GEL";
            if (pti > limit)
            {
                failures.Add(income > 0m
                    ? $"{who}: PTI {Percent(pti)} > {Percent(limit)} for income {Number(income)} GEL"
                    : $"{who}: no income");
            }
        }

        var stressNote = stress > 1m ? $" (installment stressed +{Percent(parameters.FxStressPercent)} for the foreign currency)" : string.Empty;
        return failures.Count == 0
            ? new RuleOutcome(true, measured, threshold, "PTI within the limit" + stressNote + ".")
            : new RuleOutcome(false, measured, threshold, string.Join("; ", failures) + stressNote + ".");
    }

    private static decimal LimitFor(IReadOnlyList<PtiBand> bands, decimal incomeGel) =>
        bands.First(b => b.UpToIncomeGel is not { } upTo || incomeGel < upTo).MaxPtiPercent;

    private static decimal Income(ScoringInput input, PartyFacts party) => party.MonthlyIncomes.Sum(input.ToGel);

    private static decimal Obligations(ScoringInput input, PartyFacts party) =>
        party.DeclaredObligations.Sum(input.ToGel)
        + (party.Bureau?.MonthlyObligations.Sum(input.ToGel) ?? 0m)
        + party.OwnBankInstallments;
}

/// <summary><c>LTV</c>: loan (GEL) / collateral value (GEL) within the product's maximum LTV, for secured products.</summary>
public sealed class LtvRule : ScoringRule<NoParameters>
{
    public override string Code => "LTV";

    protected override string? Validate(NoParameters parameters) => null;

    protected override RuleOutcome Evaluate(ScoringInput input, NoParameters parameters)
    {
        if (!input.Product.CollateralRequired || input.Product.MaxLtvPercent is not { } maxLtv)
        {
            return new RuleOutcome(true, null, null, "The product takes no collateral.");
        }

        var threshold = $"≤ {Percent(maxLtv)}";
        var value = input.Collaterals.Sum(c => input.ToGel(c.MarketValue));
        if (value <= 0m)
        {
            return new RuleOutcome(false, null, threshold, "No collateral was provided.");
        }

        var ltv = input.AmountGel / value * 100m; // compared exactly, rounded only for display
        return ltv <= maxLtv
            ? new RuleOutcome(true, Percent(ltv), threshold, $"LTV {Percent(ltv)} on collateral worth {Number(value)} GEL.")
            : new RuleOutcome(false, Percent(ltv), threshold,
                $"LTV {Percent(ltv)} exceeds {Percent(maxLtv)} (collateral worth {Number(value)} GEL).");
    }
}

/// <param name="MaxAgeMonths">A valuation may be at most this old (6).</param>
public sealed record ValuationAgeParameters(int MaxAgeMonths);

/// <summary><c>VALUATION_AGE</c>: every collateral valuation is at most 6 months old.</summary>
public sealed class ValuationAgeRule : ScoringRule<ValuationAgeParameters>
{
    public override string Code => "VALUATION_AGE";

    protected override string? Validate(ValuationAgeParameters parameters) =>
        parameters.MaxAgeMonths < 1 ? "months must be positive." : null;

    protected override RuleOutcome Evaluate(ScoringInput input, ValuationAgeParameters parameters)
    {
        var oldest = input.Collaterals.Count == 0 ? (DateOnly?)null : input.Collaterals.Min(c => c.ValuationDate);
        var threshold = $"not before {input.Today.AddMonths(-parameters.MaxAgeMonths):yyyy-MM-dd}";
        if (oldest is not { } date)
        {
            return new RuleOutcome(true, null, null, "No collateral to value.");
        }

        return date >= input.Today.AddMonths(-parameters.MaxAgeMonths)
            ? new RuleOutcome(true, $"{date:yyyy-MM-dd}", threshold, $"Valuations are at most {parameters.MaxAgeMonths} months old.")
            : new RuleOutcome(false, $"{date:yyyy-MM-dd}", threshold,
                $"A valuation from {date:yyyy-MM-dd} is older than {parameters.MaxAgeMonths} months.");
    }
}

/// <param name="ThresholdGel">Below this GEL equivalent a foreign-currency loan needs income in that currency.</param>
public sealed record FxIncomeParameters(decimal ThresholdGel);

/// <summary>
/// <c>FX_INCOME_CURRENCY</c>: a USD or EUR loan below the threshold is allowed only when the borrower or a co-borrower
/// has income in the loan currency.
/// </summary>
public sealed class FxIncomeCurrencyRule : ScoringRule<FxIncomeParameters>
{
    public override string Code => "FX_INCOME_CURRENCY";

    protected override string? Validate(FxIncomeParameters parameters) =>
        parameters.ThresholdGel < 0m ? "the threshold must not be negative." : null;

    protected override RuleOutcome Evaluate(ScoringInput input, FxIncomeParameters parameters)
    {
        var currency = input.Terms.Amount.Currency;
        var threshold = $"income in {currency} below {Number(parameters.ThresholdGel)} GEL";
        if (currency == Currency.GEL)
        {
            return new RuleOutcome(true, null, null, "The loan is in GEL.");
        }

        if (input.AmountGel >= parameters.ThresholdGel)
        {
            return new RuleOutcome(true, $"{Number(input.AmountGel)} GEL", threshold,
                $"At or above {Number(parameters.ThresholdGel)} GEL a {currency} loan needs no {currency} income.");
        }

        var hasIncome = input.Parties
            .Where(p => p.Role is PartyRole.Borrower or PartyRole.CoBorrower)
            .Any(p => p.MonthlyIncomes.Any(i => i.Currency == currency));
        return hasIncome
            ? new RuleOutcome(true, $"{Number(input.AmountGel)} GEL", threshold, $"The household has income in {currency}.")
            : new RuleOutcome(false, $"{Number(input.AmountGel)} GEL", threshold,
                $"A {currency} loan below {Number(parameters.ThresholdGel)} GEL needs income in {currency}.");
    }
}

/// <param name="LimitGel">The highest total exposure of one client at the bank (GEL).</param>
public sealed record ExposureParameters(decimal LimitGel);

/// <summary>
/// <c>EXPOSURE_LIMIT</c>: for every party, current exposure (loans, guarantees, pending disbursements) plus this loan
/// stays within the limit. Guarantors count the full guaranteed amount.
/// </summary>
public sealed class ExposureLimitRule : ScoringRule<ExposureParameters>
{
    public override string Code => "EXPOSURE_LIMIT";

    protected override string? Validate(ExposureParameters parameters) =>
        parameters.LimitGel <= 0m ? "the limit must be positive." : null;

    protected override RuleOutcome Evaluate(ScoringInput input, ExposureParameters parameters)
    {
        var threshold = $"≤ {Number(parameters.LimitGel)} GEL";
        var highest = input.Parties
            .Select(p => (p.Name, Total: p.ExistingExposureGel + input.AmountGel))
            .OrderByDescending(x => x.Total)
            .First();

        return highest.Total <= parameters.LimitGel
            ? new RuleOutcome(true, $"{Number(highest.Total)} GEL", threshold, "Every party stays within the exposure limit.")
            : new RuleOutcome(false, $"{Number(highest.Total)} GEL", threshold,
                $"{highest.Name}'s exposure would be {Number(highest.Total)} GEL, above {Number(parameters.LimitGel)} GEL.");
    }
}
