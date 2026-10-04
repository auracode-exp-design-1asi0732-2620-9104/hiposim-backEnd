using HipoSim.FinancialEngine;

namespace HipoSim.FinancialEngine.Tests;

public class FinancialIndicatorsTests
{
    [Fact]
    public void Npv_WhenDiscountedAtTheIrr_IsZero()
    {
        double[] cashFlows = [-1000, 300, 300, 300, 300];

        var irr = FinancialIndicators.Irr(cashFlows);

        Assert.NotNull(irr);
        Assert.Equal(0.0, FinancialIndicators.Npv(cashFlows, irr.Value), 4);
    }

    [Fact]
    public void Npv_WhenDiscountRateIsLow_IsPositive()
    {
        double[] cashFlows = [-1000, 400, 400, 400, 400];

        Assert.True(FinancialIndicators.Npv(cashFlows, 0.05) > 0);
    }

    [Fact]
    public void Irr_WhenSingleLoanPeriod_MatchesTheAgreedRate()
    {
        // Receive 1000, pay 1100 one period later: 10%.
        double[] cashFlows = [-1000, 1100];

        var irr = FinancialIndicators.Irr(cashFlows);

        Assert.NotNull(irr);
        Assert.Equal(0.10, irr.Value, 4);
    }

    [Fact]
    public void Irr_WhenThereIsNoSignChange_ReturnsNull()
    {
        double[] cashFlows = [-1000, -100, -100];

        Assert.Null(FinancialIndicators.Irr(cashFlows));
    }

    [Fact]
    public void Irr_WhenNewtonRaphsonDoesNotConverge_FallsBackToBisection()
    {
        // One Newton iteration from 0.5 cannot reach the 1e-9 tolerance, so the bisection fallback must find the 10%.
        double[] cashFlows = [-1000, 1100];

        var irr = FinancialIndicators.Irr(cashFlows, initialGuess: 0.5, maxIterations: 1);

        Assert.NotNull(irr);
        Assert.Equal(0.10, irr.Value, 6);
    }

    [Fact]
    public void Irr_WhenTheDerivativeIsZero_StopsNewtonRaphsonAndReturnsNull()
    {
        double[] cashFlows = [-1000, 0, 0];

        Assert.Null(FinancialIndicators.Irr(cashFlows));
    }

    [Fact]
    public void Irr_WhenPaymentsDoNotRecoverTheLoan_ReturnsANegativeRate()
    {
        // -1000 + 100/(1+k) + 100/(1+k)^2 = 0  ->  k = -0.6298 (the first Newton step overshoots below -100%).
        double[] cashFlows = [-1000, 100, 100];

        var irr = FinancialIndicators.Irr(cashFlows);

        Assert.NotNull(irr);
        Assert.Equal(-0.6298, irr.Value, 4);
    }

    [Fact]
    public void Calculate_WhenNoInsuranceAndLevelInstallments_TceaEqualsTheEffectiveAnnualRate()
    {
        var monthlyRate = RateConverter.AnnualToPeriodic(0.085);
        var result = AmortizationCalculator.BuildSchedule(new AmortizationRequest(
            FinancedAmount: 200000,
            MonthlyRate: monthlyRate,
            TermInMonths: 240,
            StartDate: new DateOnly(2026, 10, 1)));

        var indicators = FinancialIndicators.Calculate(
            200000, result.Schedule.Select(entry => entry.TotalPayment).ToList(), annualDiscountRate: 0.10);

        Assert.NotNull(indicators.Tcea);
        Assert.Equal(0.085, indicators.Tcea.Value, 4);
    }

    [Fact]
    public void Calculate_WhenInsuranceIsAdded_TceaIsHigherThanTheRate()
    {
        var monthlyRate = RateConverter.AnnualToPeriodic(0.085);
        var result = AmortizationCalculator.BuildSchedule(new AmortizationRequest(
            FinancedAmount: 200000,
            MonthlyRate: monthlyRate,
            TermInMonths: 240,
            StartDate: new DateOnly(2026, 10, 1),
            MonthlyPropertyInsurance: 25,
            MonthlyCreditLifeInsurance: 40));

        var indicators = FinancialIndicators.Calculate(
            200000, result.Schedule.Select(entry => entry.TotalPayment).ToList(), annualDiscountRate: 0.10);

        Assert.NotNull(indicators.Tcea);
        Assert.True(indicators.Tcea.Value > 0.085);
    }
}
