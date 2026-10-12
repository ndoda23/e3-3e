using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;

namespace CreditFlow.Domain.Scoring;

/// <summary>A late payment reported by the credit bureau.</summary>
public sealed record Delinquency(DateOnly Date, int DaysPastDue);

/// <summary>What the credit bureau knows about a person: late payments and obligations at other banks.</summary>
public sealed record BureauReport(IReadOnlyList<Delinquency> Delinquencies, IReadOnlyList<Money> MonthlyObligations);

/// <summary>Everything scoring needs about one party of the application.</summary>
/// <param name="ClientId">The client.</param>
/// <param name="Name">Display name, for explanations.</param>
/// <param name="Role">Borrower, co-borrower or guarantor.</param>
/// <param name="MonthlyIncomes">Declared net monthly incomes, each in its currency.</param>
/// <param name="DeclaredObligations">Monthly payments the client declared.</param>
/// <param name="OwnBankInstallments">Monthly installments of the client's loans at this bank (GEL equivalent).</param>
/// <param name="Bureau">The bureau report (pre-check guarantees it exists).</param>
/// <param name="ExistingExposureGel">
/// The client's current exposure at this bank in GEL: outstanding principal as borrower or co-borrower, guarantees,
/// and approved or pending disbursements (excluding this application).
/// </param>
public sealed record PartyFacts(
    ClientId ClientId,
    string Name,
    PartyRole Role,
    IReadOnlyList<Money> MonthlyIncomes,
    IReadOnlyList<Money> DeclaredObligations,
    decimal OwnBankInstallments,
    BureauReport? Bureau,
    decimal ExistingExposureGel);

/// <summary>The application facts a scoring run evaluates.</summary>
/// <param name="Terms">The requested terms.</param>
/// <param name="Product">The product snapshot terms.</param>
/// <param name="AmountGel">GEL equivalent of the amount.</param>
/// <param name="NewInstallment">The largest installment of the new loan, in the loan currency.</param>
/// <param name="Collaterals">Collateral with its valuation.</param>
/// <param name="Parties">Every party.</param>
/// <param name="Rates">Exchange rates on the scoring date, by currency (GEL included).</param>
/// <param name="Today">The bank date of scoring.</param>
public sealed record ScoringInput(
    LoanTerms Terms,
    ProductTerms Product,
    decimal AmountGel,
    Money NewInstallment,
    IReadOnlyList<Collateral> Collaterals,
    IReadOnlyList<PartyFacts> Parties,
    IReadOnlyDictionary<Currency, ExchangeRate> Rates,
    DateOnly Today)
{
    public decimal ToGel(Money amount) => Rates[amount.Currency].ToGel(amount).Amount;
}

/// <summary>What one rule found.</summary>
public sealed record RuleOutcome(bool Passed, string? Measured, string? Threshold, string Reason);

/// <summary>The logic of one scoring rule; its parameters come from the rule set (JSON).</summary>
public interface IScoringRule
{
    string Code { get; }

    /// <summary>A description of what is wrong with the parameters, or <c>null</c> when they are valid.</summary>
    string? ValidateParameters(string parametersJson);

    RuleOutcome Evaluate(ScoringInput input, string parametersJson);
}

/// <summary>Runs the rules of a rule set and keeps the explained result of each.</summary>
public sealed class ScoringEngine
{
    private readonly Dictionary<string, IScoringRule> _rules;

    public ScoringEngine(IEnumerable<IScoringRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.ToDictionary(r => r.Code, StringComparer.Ordinal);
    }

    /// <summary>The rules this engine knows (all rules of the system).</summary>
    public static ScoringEngine Standard() => new(
    [
        new ProductLimitsRule(), new BureauDelinquencyRule(), new PtiRule(), new LtvRule(), new ValuationAgeRule(),
        new FxIncomeCurrencyRule(), new ExposureLimitRule(),
    ]);

    public IReadOnlyCollection<string> KnownCodes => _rules.Keys;

    public IEnumerable<Error> Validate(RuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_rules.TryGetValue(definition.Code, out var rule))
        {
            yield return ScoringErrors.UnknownRule(definition.Code);
        }
        else if (rule.ValidateParameters(definition.ParametersJson) is { } problem)
        {
            yield return ScoringErrors.InvalidParameters(definition.Code, problem);
        }
    }

    public ScoringRun Score(ScoringRuleSet ruleSet, ScoringInput input, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(input);

        var results = ruleSet.Rules
            .Select(definition =>
            {
                var outcome = _rules[definition.Code].Evaluate(input, definition.ParametersJson);
                return new RuleResult(
                    definition.Code, outcome.Passed, definition.Overridable, outcome.Measured, outcome.Threshold, outcome.Reason);
            })
            .ToList();
        return new ScoringRun(ruleSet.Version, at, results);
    }
}
