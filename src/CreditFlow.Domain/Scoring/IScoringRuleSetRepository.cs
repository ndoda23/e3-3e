namespace CreditFlow.Domain.Scoring;

/// <summary>Persistence of <see cref="ScoringRuleSet"/> versions (each version is immutable).</summary>
public interface IScoringRuleSetRepository
{
    /// <summary>The newest version, used for new scoring runs.</summary>
    Task<ScoringRuleSet?> GetLatestAsync(CancellationToken cancellationToken);

    Task<ScoringRuleSet?> GetAsync(int version, CancellationToken cancellationToken);

    void Add(ScoringRuleSet ruleSet);
}
