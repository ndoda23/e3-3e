using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Scoring;

namespace CreditFlow.Application.UnitTests.ReferenceData;

public sealed class ScoringRuleSetSeedTests
{
    [Fact]
    public void Version_1_is_valid_and_uses_every_rule_of_the_engine()
    {
        var engine = ScoringEngine.Standard();

        var set = ScoringRuleSet.Create(1, ScoringRuleSetSeed.Version1, engine, DateTimeOffset.UtcNow);

        Assert.True(set.IsSuccess, set.Error?.Description);
        Assert.Equal(engine.KnownCodes.Order(), ScoringRuleSetSeed.Version1.Select(r => r.Code).Order());
    }

    [Fact]
    public void Only_PTI_can_be_overridden()
    {
        Assert.Equal(["PTI"], ScoringRuleSetSeed.Version1.Where(r => r.Overridable).Select(r => r.Code));
    }

    [Theory]
    [InlineData("PTI", "\"maxPtiPercent\":35")]
    [InlineData("PTI", "\"maxPtiPercent\":45")]
    [InlineData("PTI", "\"maxPtiPercent\":55")]
    [InlineData("PTI", "\"fxStressPercent\":20")]
    [InlineData("FX_INCOME_CURRENCY", "\"thresholdGel\":200000")]
    [InlineData("EXPOSURE_LIMIT", "\"limitGel\":300000")]
    [InlineData("VALUATION_AGE", "\"maxAgeMonths\":6")]
    [InlineData("BUREAU_DPD30_12M", "\"minDaysPastDue\":30")]
    public void Parameters_are_the_approved_values(string code, string fragment)
    {
        Assert.Contains(fragment, ScoringRuleSetSeed.Version1.Single(r => r.Code == code).ParametersJson, StringComparison.Ordinal);
    }
}
