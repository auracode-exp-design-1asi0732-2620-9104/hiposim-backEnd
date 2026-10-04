using HipoSim.FinancialEngine;

namespace HipoSim.FinancialEngine.Tests;

public class RateConverterTests
{
    [Fact]
    public void ToMonthlyEffectiveRate_WhenEffectiveAnnual_ReturnsMonthlyRate()
    {
        // TEA 10.7% -> about 0.8507% monthly (AutoFinance Pro case 1)
        var monthly = RateConverter.ToMonthlyEffectiveRate(RateType.Effective, 0.107, null);

        Assert.Equal(0.0085071, monthly, 5);
    }

    [Fact]
    public void NominalToEffectiveAnnual_WhenMonthlyCapitalization_ReturnsTea()
    {
        // TEA = (1 + 0.12 / 12)^12 - 1 = 12.6825%
        var tea = RateConverter.NominalToEffectiveAnnual(0.12, CapitalizationFrequency.Monthly);

        Assert.Equal(0.126825, tea, 6);
    }

    [Fact]
    public void ToMonthlyEffectiveRate_WhenNominalWithoutFrequency_Throws()
    {
        Assert.Throws<ArgumentException>(() => RateConverter.ToMonthlyEffectiveRate(RateType.Nominal, 0.12, null));
    }

    [Fact]
    public void ToMonthlyEffectiveRate_WhenRateTypeIsUnknown_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RateConverter.ToMonthlyEffectiveRate((RateType)99, 0.12, null));
    }

    [Fact]
    public void NominalToEffectiveAnnual_WhenAnnualCapitalization_ReturnsSameRate()
    {
        Assert.Equal(0.12, RateConverter.NominalToEffectiveAnnual(0.12, CapitalizationFrequency.Annual), 12);
    }

    [Fact]
    public void MonthlyToEffectiveAnnual_WhenConvertedBackFromMonthly_RoundTripsTheAnnualRate()
    {
        var monthly = RateConverter.AnnualToPeriodic(0.085);

        Assert.Equal(0.085, RateConverter.MonthlyToEffectiveAnnual(monthly), 12);
    }
}
