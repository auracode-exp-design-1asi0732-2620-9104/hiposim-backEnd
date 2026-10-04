using HipoSim.FinancialEngine;

namespace HipoSim.FinancialEngine.Tests;

/// <summary>
/// Cases 1 to 3 are the documented AutoFinance Pro cases (informe, section 7), with the same
/// tolerances as the Python tests (the report values come from rounded screenshots).
/// </summary>
public class AmortizationCalculatorTests
{
    [Fact]
    public void BuildSchedule_WhenStandardLoanWithBalloon_MatchesInformeCase1()
    {
        var monthlyRate = RateConverter.ToMonthlyEffectiveRate(RateType.Effective, 0.107, null);

        var result = AmortizationCalculator.BuildSchedule(new AmortizationRequest(
            FinancedAmount: 68000,
            MonthlyRate: monthlyRate,
            TermInMonths: 60,
            StartDate: new DateOnly(2026, 5, 14),
            BalloonRatio: 0.25,
            MonthlyPropertyInsurance: 150,
            MonthlyCreditLifeInsurance: 45));

        var first = result.Schedule[0];
        Assert.Equal(1088.84, result.RegularInstallment, 0);
        Assert.InRange(first.Interest, 577.48, 579.48);
        Assert.InRange(first.Amortization, 509.36, 511.36);
        Assert.InRange(first.RemainingBalance, 67488.64, 67490.64);
        Assert.Equal(60, result.Schedule.Count);
        Assert.True(result.Schedule[^1].IsBalloonPayment);
        Assert.Equal(0.0, result.Schedule[^1].RemainingBalance, 2);
    }

    [Fact]
    public void BuildSchedule_WhenPartialGrace_MatchesInformeCase2()
    {
        var monthlyRate = RateConverter.ToMonthlyEffectiveRate(RateType.Effective, 0.118, null);

        var result = AmortizationCalculator.BuildSchedule(new AmortizationRequest(
            FinancedAmount: 102000,
            MonthlyRate: monthlyRate,
            TermInMonths: 72,
            StartDate: new DateOnly(2026, 5, 15),
            GraceType: GraceType.Partial,
            GraceMonths: 3,
            BalloonRatio: 0.30,
            MonthlyPropertyInsurance: 150,
            MonthlyCreditLifeInsurance: 45));

        foreach (var entry in result.Schedule.Take(3))
        {
            Assert.True(entry.IsGracePeriod);
            Assert.Equal(0.0, entry.Amortization, 2);
            Assert.InRange(entry.RemainingBalance, 101999.5, 102000.5);
            Assert.InRange(entry.Interest, 951.52, 953.52);
            Assert.InRange(entry.Installment, 951.52, 953.52);
        }

        var firstRegular = result.Schedule[3];
        Assert.False(firstRegular.IsGracePeriod);
        Assert.InRange(firstRegular.Interest, 951.52, 953.52);
        Assert.InRange(firstRegular.Amortization, 454.86, 456.86);
        Assert.InRange(firstRegular.Installment, 1407.38, 1409.38);
        Assert.Equal(72, result.Schedule.Count);
        Assert.Equal(0.0, result.Schedule[^1].RemainingBalance, 2);
    }

    [Fact]
    public void BuildSchedule_WhenTotalGrace_CapitalizesInterestInInformeCase3()
    {
        var monthlyRate = RateConverter.ToMonthlyEffectiveRate(RateType.Nominal, 0.12, CapitalizationFrequency.Monthly);

        var result = AmortizationCalculator.BuildSchedule(new AmortizationRequest(
            FinancedAmount: 24000,
            MonthlyRate: monthlyRate,
            TermInMonths: 48,
            StartDate: new DateOnly(2026, 5, 15),
            GraceType: GraceType.Total,
            GraceMonths: 2,
            BalloonRatio: 0.20,
            MonthlyPropertyInsurance: 150,
            MonthlyCreditLifeInsurance: 45));

        var month1 = result.Schedule[0];
        var month2 = result.Schedule[1];
        Assert.Equal(0.0, month1.Installment);
        Assert.Equal(0.0, month1.Amortization);
        Assert.True(month1.RemainingBalance > 24000);
        Assert.True(month2.RemainingBalance > month1.RemainingBalance);
        Assert.Equal(48, result.Schedule.Count);
        Assert.Equal(0.0, result.Schedule[^1].RemainingBalance, 2);
    }

    [Fact]
    public void BuildSchedule_WhenRateIsZero_SplitsCapitalEvenly()
    {
        var result = AmortizationCalculator.BuildSchedule(new AmortizationRequest(
            FinancedAmount: 12000,
            MonthlyRate: 0.0,
            TermInMonths: 12,
            StartDate: new DateOnly(2026, 1, 1)));

        Assert.Equal(1000.0, result.RegularInstallment, 2);
        Assert.Equal(0.0, result.Schedule[^1].RemainingBalance, 2);
    }

    [Fact]
    public void BuildSchedule_WhenNoBalloon_DoesNotFlagTheLastInstallmentAsBalloon()
    {
        var result = AmortizationCalculator.BuildSchedule(new AmortizationRequest(
            FinancedAmount: 200000,
            MonthlyRate: 0.0068,
            TermInMonths: 240,
            StartDate: new DateOnly(2026, 10, 1)));

        Assert.DoesNotContain(result.Schedule, entry => entry.IsBalloonPayment);
        Assert.Equal(0.0, result.BalloonAmount, 2);
    }

    [Fact]
    public void BuildSchedule_WhenNoGrace_AmortizationAddsUpToTheFinancedAmount()
    {
        var result = AmortizationCalculator.BuildSchedule(new AmortizationRequest(
            FinancedAmount: 219800,
            MonthlyRate: RateConverter.AnnualToPeriodic(0.085),
            TermInMonths: 240,
            StartDate: new DateOnly(2026, 10, 1)));

        var amortized = result.Schedule.Sum(entry => entry.Amortization);

        // Each row is rounded to cents, so the sum can drift a few cents over 240 rows.
        Assert.Equal(219800, amortized, 0);
    }

    [Fact]
    public void BuildSchedule_WhenStartingOnTheThirtyFirst_ClipsToMonthEndAndKeepsTheClippedDay()
    {
        var result = AmortizationCalculator.BuildSchedule(new AmortizationRequest(
            FinancedAmount: 10000,
            MonthlyRate: 0.01,
            TermInMonths: 3,
            StartDate: new DateOnly(2026, 1, 31)));

        Assert.Equal(new DateOnly(2026, 2, 28), result.Schedule[0].PaymentDate);
        Assert.Equal(new DateOnly(2026, 3, 28), result.Schedule[1].PaymentDate);
        Assert.Equal(new DateOnly(2026, 4, 28), result.Schedule[2].PaymentDate);
    }

    [Fact]
    public void BuildSchedule_WhenInsuranceIsSet_AddsItToEachTotalPayment()
    {
        var result = AmortizationCalculator.BuildSchedule(new AmortizationRequest(
            FinancedAmount: 12000,
            MonthlyRate: 0.0,
            TermInMonths: 12,
            StartDate: new DateOnly(2026, 1, 1),
            MonthlyPropertyInsurance: 20,
            MonthlyCreditLifeInsurance: 10.5));

        Assert.All(result.Schedule, entry => Assert.Equal(1030.5, entry.TotalPayment, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void FrenchInstallment_WhenPeriodsAreNotPositive_Throws(int periods)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AmortizationCalculator.FrenchInstallment(10000, 0.01, periods));
    }

    [Fact]
    public void BuildSchedule_WhenMonthlyRateIsNegative_Throws()
    {
        var request = new AmortizationRequest(
            FinancedAmount: 10000,
            MonthlyRate: -0.01,
            TermInMonths: 12,
            StartDate: new DateOnly(2026, 1, 1));

        Assert.Throws<ArgumentException>(() => AmortizationCalculator.BuildSchedule(request));
    }

    [Theory]
    [InlineData(0, 12, GraceType.None, 0, 0.0)]
    [InlineData(10000, 0, GraceType.None, 0, 0.0)]
    [InlineData(10000, 12, GraceType.Total, 12, 0.0)]
    [InlineData(10000, 12, GraceType.None, 3, 0.0)]
    [InlineData(10000, 12, GraceType.Partial, 0, 0.0)]
    [InlineData(10000, 12, GraceType.None, 0, 1.0)]
    [InlineData(10000, 12, GraceType.None, 0, -0.1)]
    public void BuildSchedule_WhenRequestIsInconsistent_Throws(
        double financedAmount, int term, GraceType graceType, int graceMonths, double balloonRatio)
    {
        var request = new AmortizationRequest(
            FinancedAmount: financedAmount,
            MonthlyRate: 0.01,
            TermInMonths: term,
            StartDate: new DateOnly(2026, 1, 1),
            GraceType: graceType,
            GraceMonths: graceMonths,
            BalloonRatio: balloonRatio);

        Assert.Throws<ArgumentException>(() => AmortizationCalculator.BuildSchedule(request));
    }
}
