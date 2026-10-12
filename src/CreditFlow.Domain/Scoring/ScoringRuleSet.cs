using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Scoring;

/// <summary>
/// One rule in a rule set: which rule (its logic is code, see <see cref="IScoringRule"/>), its parameters (JSON) and
/// whether a failure may be overridden by an authorized decider.
/// </summary>
public sealed record RuleDefinition(string Code, bool Overridable, string ParametersJson);

/// <summary>
/// A published version of the scoring rules. Versions are immutable; changing a rule publishes the next version. The
/// newest version scores new runs, and every run records the version it used, so decisions stay reproducible.
/// </summary>
public sealed class ScoringRuleSet
{
    private readonly List<RuleDefinition> _rules = [];

    private ScoringRuleSet()
    {
    }

    public int Version { get; private set; }

    public DateTimeOffset PublishedAt { get; private set; }

    public IReadOnlyList<RuleDefinition> Rules => _rules;

    /// <param name="version">The version number (1 for the first).</param>
    /// <param name="rules">The rules, in evaluation order.</param>
    /// <param name="engine">Validates that every rule is known and its parameters are valid.</param>
    /// <param name="publishedAt">When the version was published.</param>
    public static Result<ScoringRuleSet> Create(
        int version, IReadOnlyList<RuleDefinition> rules, ScoringEngine engine, DateTimeOffset publishedAt)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(engine);

        var errors = new List<Error>();
        if (version < 1)
        {
            errors.Add(ScoringErrors.InvalidVersion);
        }

        if (rules.Count == 0 || rules.Select(r => r.Code).Distinct(StringComparer.Ordinal).Count() != rules.Count)
        {
            errors.Add(ScoringErrors.RulesMustBeUnique);
        }

        errors.AddRange(rules.SelectMany(engine.Validate));
        if (errors.Count > 0)
        {
            return Result.Failure<ScoringRuleSet>(Error.ValidationFailed(errors));
        }

        var set = new ScoringRuleSet { Version = version, PublishedAt = publishedAt };
        set._rules.AddRange(rules);
        return Result.Success(set);
    }

    /// <summary>The next version with changed rules.</summary>
    public Result<ScoringRuleSet> PublishNext(IReadOnlyList<RuleDefinition> rules, ScoringEngine engine, DateTimeOffset at) =>
        Create(Version + 1, rules, engine, at);
}

public static class ScoringErrors
{
    public static readonly Error InvalidVersion = Error.Validation(
        "Scoring.InvalidVersion", "A rule-set version starts at 1.", "Version");

    public static readonly Error RulesMustBeUnique = Error.Validation(
        "Scoring.RulesMustBeUnique", "A rule set has at least one rule and each rule once.", "Rules");

    public static Error UnknownRule(string code) => Error.Validation(
        "Scoring.UnknownRule", $"'{code}' is not a known scoring rule.", "Rules");

    public static Error InvalidParameters(string code, string detail) => Error.Validation(
        "Scoring.InvalidParameters", $"The parameters of {code} are not valid: {detail}", "Rules");
}
