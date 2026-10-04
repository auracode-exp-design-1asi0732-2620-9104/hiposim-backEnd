using HipoSim.FinancialEngine;

namespace HipoSim.FinancialEngine.Tests;

public class BenefitsEngineTests
{
    private readonly BenefitsEngine _engine = new();

    [Theory]
    [InlineData(120_000, 26_400)]
    [InlineData(200_000, 26_400)]
    [InlineData(200_001, 18_200)]
    [InlineData(280_000, 18_200)]
    [InlineData(300_000, 18_200)]
    [InlineData(300_001, 10_000)]
    [InlineData(400_000, 10_000)]
    public void EvaluateGoodPayerBonus_WhenPriceIsInRangeAndRequested_AppliesTheTierAmount(double price, double expectedAmount)
    {
        var result = _engine.EvaluateGoodPayerBonus(price, apply: true);

        Assert.True(result.Eligible);
        Assert.Equal(expectedAmount, result.AppliedAmount);
        Assert.Equal("Good Payer Bonus", result.Name);
    }

    [Theory]
    [InlineData(400_001)]
    [InlineData(800_000)]
    public void EvaluateGoodPayerBonus_WhenPriceIsOutOfRange_IsNotEligibleAndAppliesNothing(double price)
    {
        var result = _engine.EvaluateGoodPayerBonus(price, apply: true);

        Assert.False(result.Eligible);
        Assert.Equal(0, result.AppliedAmount);
    }

    [Fact]
    public void EvaluateGoodPayerBonus_WhenNotRequested_ReportsEligibilityButAppliesNothing()
    {
        var result = _engine.EvaluateGoodPayerBonus(280_000, apply: false);

        Assert.True(result.Eligible);
        Assert.Equal(0, result.AppliedAmount);
    }

    [Fact]
    public void EvaluateGoodPayerBonus_WhenCustomTiersAreGivenInAnyOrder_UsesTheSmallestMatchingTier()
    {
        var engine = new BenefitsEngine(
        [
            new GoodPayerBonusTier(500_000, 5_000),
            new GoodPayerBonusTier(250_000, 20_000),
        ]);

        Assert.Equal(20_000, engine.EvaluateGoodPayerBonus(240_000, apply: true).AppliedAmount);
        Assert.Equal(5_000, engine.EvaluateGoodPayerBonus(300_000, apply: true).AppliedAmount);
    }
}
