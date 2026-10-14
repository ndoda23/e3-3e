namespace CreditFlow.Domain.Scoring;

/// <summary>
/// The result of one scoring rule, kept so that every decision is explainable: what was measured, against which
/// threshold, and why the rule passed or failed.
/// </summary>
/// <param name="RuleCode">Rule code from the rule set (e.g. <c>PTI</c>).</param>
/// <param name="Passed">Whether the rule passed.</param>
/// <param name="Overridable">A failure of this rule can be overridden by an authorized decider with a reason.</param>
/// <param name="Measured">The measured value, formatted (e.g. <c>52.30 %</c>).</param>
/// <param name="Threshold">The threshold it was compared with, formatted (e.g. <c>≤ 45.00 %</c>).</param>
/// <param name="Reason">Plain-language explanation.</param>
public sealed record RuleResult(
    string RuleCode, bool Passed, bool Overridable, string? Measured, string? Threshold, string Reason);

/// <summary>One complete scoring of an application with a given rule-set version.</summary>
public sealed record ScoringRun(int RuleSetVersion, DateTimeOffset ScoredAt, IReadOnlyList<RuleResult> Results)
{
    /// <summary>A rule that cannot be overridden failed: the application is rejected automatically.</summary>
    public bool HasHardFailure => Results.Any(r => !r.Passed && !r.Overridable);

    /// <summary>Only overridable rules failed: approval needs an override with a reason.</summary>
    public bool RequiresOverride => !HasHardFailure && Results.Any(r => !r.Passed);

    public IEnumerable<RuleResult> Failures => Results.Where(r => !r.Passed);
}
