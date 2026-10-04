using HipoSim.FinancialEngine;
using HipoSim.Simulations;

namespace HipoSim.Simulations.Tests;

public class SimulationRequestValidatorTests
{
    private readonly SimulationRequestValidator _validator = new();

    private static SimulationRequest ValidRequest() => new()
    {
        PropertyPrice = 280_000,
        DownPayment = 42_000,
        AnnualEffectiveRate = 0.085,
        TermInMonths = 240,
    };

    [Fact]
    public void Validate_WhenOnlyRequiredFieldsAreGiven_AppliesTheDefaults()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
        Assert.Equal(0, result.Value!.GracePeriodMonths);
        Assert.Equal(GraceType.None, result.Value.GraceType);
        Assert.False(result.Value.ApplyGoodPayerBonus);
        Assert.Null(result.Value.StartDate);
    }

    [Fact]
    public void Validate_WhenRequestIsEmpty_ListsEveryRequiredField()
    {
        var result = _validator.Validate(new SimulationRequest());

        Assert.False(result.IsValid);
        Assert.Equal(
            ["annualEffectiveRate", "downPayment", "propertyPrice", "termInMonths"],
            result.Errors.Keys.Order().ToArray());
        Assert.Equal("The propertyPrice field is required.", result.Errors["propertyPrice"][0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-280_000)]
    public void Validate_WhenPropertyPriceIsNotPositive_ReportsOnlyThePrice(double propertyPrice)
    {
        var result = _validator.Validate(ValidRequest() with { PropertyPrice = propertyPrice });

        Assert.Equal(["propertyPrice"], result.Errors.Keys.ToArray());
        Assert.Equal("The propertyPrice must be greater than 0.", result.Errors["propertyPrice"][0]);
    }

    [Fact]
    public void Validate_WhenDownPaymentIsNegative_ReportsOnlyTheDownPayment()
    {
        var result = _validator.Validate(ValidRequest() with { DownPayment = -1 });

        Assert.Equal(["downPayment"], result.Errors.Keys.ToArray());
        Assert.Equal("The downPayment cannot be negative.", result.Errors["downPayment"][0]);
    }

    [Theory]
    [InlineData(27_999, false)]
    [InlineData(28_000, true)]
    [InlineData(280_000, true)]
    [InlineData(280_001, false)]
    public void Validate_WhenDownPaymentIsAroundTheLimits_AppliesTheTenPercentRule(double downPayment, bool expectedValid)
    {
        var result = _validator.Validate(ValidRequest() with { DownPayment = downPayment });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Validate_WhenDownPaymentIsBelowTenPercent_UsesTheDocumentedMessage()
    {
        var result = _validator.Validate(ValidRequest() with { DownPayment = 10_000 });

        Assert.Equal(
            ["The down payment must be at least 10% of the property price."],
            result.Errors["downPayment"]);
    }

    [Fact]
    public void Validate_WhenTenPercentIsNotExactlyRepresentableInBinary_StillAcceptsExactlyTenPercent()
    {
        // 0.1 * 350000.1 is not exactly 35000.01 in binary floating point; the rule is checked in decimal.
        var result = _validator.Validate(ValidRequest() with { PropertyPrice = 350_000.10, DownPayment = 35_000.01 });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(8.5)]
    [InlineData(0.0)]
    [InlineData(-0.05)]
    [InlineData(1.01)]
    public void Validate_WhenRateIsNotAFraction_Fails(double rate)
    {
        var result = _validator.Validate(ValidRequest() with { AnnualEffectiveRate = rate });

        Assert.Contains("annualEffectiveRate", result.Errors.Keys);
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(360, true)]
    [InlineData(361, false)]
    public void Validate_WhenTermIsAroundTheLimits_AppliesTheRange(int term, bool expectedValid)
    {
        var result = _validator.Validate(ValidRequest() with { TermInMonths = term });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("total", 6, GraceType.Total)]
    [InlineData("Partial", 3, GraceType.Partial)]
    [InlineData(" TOTAL ", 12, GraceType.Total)]
    [InlineData("none", 0, GraceType.None)]
    [InlineData(null, 0, GraceType.None)]
    public void Validate_WhenGraceIsConsistent_ParsesTheType(string? graceType, int months, GraceType expected)
    {
        var result = _validator.Validate(ValidRequest() with { GraceType = graceType, GracePeriodMonths = months });

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Value!.GraceType);
    }

    [Theory]
    [InlineData("none", 6, "graceType")]
    [InlineData(null, 6, "graceType")]
    [InlineData("total", 0, "gracePeriodMonths")]
    [InlineData("partial", null, "gracePeriodMonths")]
    [InlineData("sometimes", 0, "graceType")]
    [InlineData("total", -1, "gracePeriodMonths")]
    [InlineData("total", 240, "gracePeriodMonths")]
    public void Validate_WhenGraceIsInconsistent_ReportsTheField(string? graceType, int? months, string expectedField)
    {
        var result = _validator.Validate(ValidRequest() with { GraceType = graceType, GracePeriodMonths = months });

        Assert.Contains(expectedField, result.Errors.Keys);
    }

    [Fact]
    public void Validate_WhenSeveralRulesFail_ReportsAllOfThemAtOnce()
    {
        var request = new SimulationRequest
        {
            PropertyPrice = 280_000,
            DownPayment = 1_000,
            AnnualEffectiveRate = 8.5,
            TermInMonths = 12,
        };

        var result = _validator.Validate(request);

        Assert.Equal(["annualEffectiveRate", "downPayment", "termInMonths"], result.Errors.Keys.Order().ToArray());
    }
}
