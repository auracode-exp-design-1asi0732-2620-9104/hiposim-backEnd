using HipoSim.FinancialEngine;
using Microsoft.Extensions.Options;

namespace HipoSim.Simulations;

public interface ISimulationService
{
    /// <exception cref="SimulationValidationException">The request breaks a validation rule.</exception>
    Task<SimulationResponse> CalculateAsync(SimulationRequest request, Guid? buyerId, CancellationToken cancellationToken = default);
}

/// <summary>Validates the request, applies the bonus, runs the financial engine and persists the result.</summary>
public sealed class SimulationService(
    SimulationRequestValidator validator,
    BenefitsEngine benefitsEngine,
    FinancialEngine.FinancialEngine financialEngine,
    IOptions<SimulationDefaults> defaults,
    ISimulationRepository repository,
    TimeProvider timeProvider) : ISimulationService
{
    public async Task<SimulationResponse> CalculateAsync(
        SimulationRequest request,
        Guid? buyerId,
        CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(request);
        if (validation.Value is not { } validated)
        {
            throw new SimulationValidationException(validation.Errors);
        }

        var assumptions = defaults.Value;
        var benefit = benefitsEngine.EvaluateGoodPayerBonus(validated.PropertyPrice, validated.ApplyGoodPayerBonus);

        var toFinance = Math.Round(validated.PropertyPrice - validated.DownPayment - benefit.AppliedAmount, 2);
        if (toFinance <= 0)
        {
            throw new SimulationValidationException(new Dictionary<string, string[]>
            {
                ["downPayment"] = ["The down payment and the bonus leave nothing to finance."],
            });
        }

        var input = new LoanSimulationInput(
            PropertyPrice: validated.PropertyPrice,
            DownPayment: validated.DownPayment,
            RateType: RateType.Effective,
            AnnualRate: validated.AnnualEffectiveRate,
            TermInMonths: validated.TermInMonths,
            StartDate: validated.StartDate ?? TodayInLima(),
            GraceType: validated.GraceType,
            GraceMonths: validated.GracePeriodMonths,
            BenefitAmount: benefit.AppliedAmount,
            MonthlyPropertyInsurance: assumptions.MonthlyPropertyInsurance,
            MonthlyCreditLifeInsurance: assumptions.MonthlyCreditLifeInsurance,
            AdministrativeExpenses: assumptions.AdministrativeExpenses,
            AnnualDiscountRate: assumptions.AnnualDiscountRate);

        var result = financialEngine.Simulate(input);
        var summary = result.Summary;

        var openingBalance = summary.FinancedAmount;
        var schedule = new List<AmortizationEntryResponse>(result.Schedule.Count);
        foreach (var entry in result.Schedule)
        {
            schedule.Add(new AmortizationEntryResponse(
                entry.Period,
                entry.PaymentDate,
                openingBalance,
                entry.Installment,
                entry.Interest,
                entry.Amortization,
                entry.RemainingBalance,
                entry.PropertyInsurance,
                entry.CreditLifeInsurance,
                entry.TotalPayment,
                entry.IsGracePeriod));
            openingBalance = entry.RemainingBalance;
        }

        var response = new SimulationResponse(
            SimulationId: Guid.NewGuid(),
            FinancedAmount: summary.FinancedAmount,
            MonthlyInstallment: summary.RegularInstallment,
            TotalPaid: summary.TotalPaid,
            TotalInterest: summary.TotalInterest,
            AdditionalCosts: summary.AdditionalCosts,
            Tcea: summary.Tcea,
            Irr: summary.AnnualIrr,
            Npv: summary.Npv,
            Benefit: new BenefitResponse(benefit.Name, benefit.Eligible, benefit.AppliedAmount),
            Assumptions: new AssumptionsResponse(
                assumptions.MonthlyPropertyInsurance,
                assumptions.MonthlyCreditLifeInsurance,
                assumptions.AdministrativeExpenses,
                assumptions.AnnualDiscountRate),
            AmortizationSchedule: schedule);

        await repository.AddAsync(
            new SimulationRecord(response.SimulationId, buyerId, timeProvider.GetUtcNow(), input, response),
            cancellationToken);

        return response;
    }

    // Peru does not observe daylight saving time, so Lima is always UTC-5.
    private DateOnly TodayInLima() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime.AddHours(-5));
}
