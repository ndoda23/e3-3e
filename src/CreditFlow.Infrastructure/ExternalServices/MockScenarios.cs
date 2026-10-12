using Microsoft.Extensions.Options;

namespace CreditFlow.Infrastructure.ExternalServices;

/// <summary>What a mock external service does for a personal number.</summary>
public enum MockScenario
{
    /// <summary>The person is on the sanctions list (seeded in <c>SanctionsListEntries</c>).</summary>
    SanctionsMatch = 1,

    /// <summary>The person is a politically exposed person (seeded in <c>SanctionsListEntries</c>).</summary>
    PepMatch = 2,

    /// <summary>KYC cannot verify the identity document.</summary>
    KycFailure = 3,

    /// <summary>The bureau reports a 45-day late payment three months ago.</summary>
    BureauDelinquency = 4,

    /// <summary>The bureau never answers (unavailable).</summary>
    BureauUnavailable = 5,

    /// <summary>The bureau times out on the first two requests, then answers.</summary>
    BureauTimeoutTwice = 6,

    /// <summary>The bureau reports 2,500 GEL of monthly obligations at other banks.</summary>
    BureauHighObligations = 7,

    /// <summary>Core banking rejects the transfer (no money moved).</summary>
    CoreBankingError = 8,

    /// <summary>Core banking makes the transfer but the answer times out.</summary>
    CoreBankingTimeout = 9,
}

/// <summary>
/// Mock settings (configuration section <c>Mocks</c>). The seeded test personal numbers
/// (<see cref="MockPersonalNumbers"/>) always apply; <see cref="Scenarios"/> adds or overrides scenarios by personal
/// number, e.g. <c>"Mocks:Scenarios:01001011099:0": "BureauUnavailable"</c>.
/// </summary>
public sealed class MockOptions
{
    public const string SectionName = "Mocks";

    public Dictionary<string, MockScenario[]> Scenarios { get; set; } = [];

    /// <summary>How long the bureau may take before the request counts as unavailable.</summary>
    public TimeSpan BureauTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a mock "hangs" in a timeout scenario (longer than the timeouts).</summary>
    public TimeSpan HangDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long core banking may take before the request counts as timed out.</summary>
    public TimeSpan CoreBankingTimeout { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>The seeded test personal numbers and the scenario each one triggers (also listed in the README).</summary>
public static class MockPersonalNumbers
{
    public const string Clean = "01001011001";
    public const string Sanctioned = "01001011002";
    public const string Pep = "01001011003";
    public const string KycFailure = "01001011004";
    public const string LatePayer = "01001011005";
    public const string BureauDown = "01001011006";
    public const string BureauSlow = "01001011007";
    public const string HighObligations = "01001011008";
    public const string CoreBankingError = "01001011009";
    public const string CoreBankingTimeout = "01001011010";

    public static IReadOnlyDictionary<string, MockScenario[]> Scenarios { get; } = new Dictionary<string, MockScenario[]>
    {
        [Sanctioned] = [MockScenario.SanctionsMatch],
        [Pep] = [MockScenario.PepMatch],
        [KycFailure] = [MockScenario.KycFailure],
        [LatePayer] = [MockScenario.BureauDelinquency],
        [BureauDown] = [MockScenario.BureauUnavailable],
        [BureauSlow] = [MockScenario.BureauTimeoutTwice],
        [HighObligations] = [MockScenario.BureauHighObligations],
        [CoreBankingError] = [MockScenario.CoreBankingError],
        [CoreBankingTimeout] = [MockScenario.CoreBankingTimeout],
    };
}

/// <summary>Resolves the scenarios of a personal number: the seeded catalog plus configuration.</summary>
public sealed class MockScenarios(IOptionsMonitor<MockOptions> options)
{
    public bool Has(string personalNumber, MockScenario scenario) =>
        options.CurrentValue.Scenarios.TryGetValue(personalNumber, out var configured)
            ? configured.Contains(scenario)
            : MockPersonalNumbers.Scenarios.TryGetValue(personalNumber, out var seeded) && seeded.Contains(scenario);

    public MockOptions Options => options.CurrentValue;
}
