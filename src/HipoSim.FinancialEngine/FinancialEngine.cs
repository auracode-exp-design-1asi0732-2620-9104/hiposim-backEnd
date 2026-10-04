namespace HipoSim.FinancialEngine;

/// <param name="AnnualRate">Fraction (0.085 = 8.5%); a nominal rate (TNA) when <paramref name="RateType"/> is Nominal.</param>
/// <param name="BalloonRatio">Fraction of the financed amount left for the last installment (0 for a mortgage).</param>
/// <param name="BenefitAmount">State benefit already evaluated by <see cref="BenefitsEngine"/>; it reduces the financed amount.</param>
/// <param name="AnnualDiscountRate">Discount rate (COK) for the NPV; the engine default is used when null.</param>
public sealed record LoanSimulationInput(
    double PropertyPrice,
    double DownPayment,
    RateType RateType,
    double AnnualRate,
    int TermInMonths,
    DateOnly StartDate,
    CapitalizationFrequency? CapitalizationFrequency = null,
    GraceType GraceType = GraceType.None,
    int GraceMonths = 0,
    double BalloonRatio = 0,
    double BenefitAmount = 0,
    double MonthlyPropertyInsurance = 0,
    double MonthlyCreditLifeInsurance = 0,
    double AdministrativeExpenses = 0,
    double? AnnualDiscountRate = null);

public sealed record SimulationSummary(
    double FinancedAmount,
    double RegularInstallment,
    double BalloonAmount,
    double TotalPaid,
    double TotalInterest,
    double AdditionalCosts,
    double Npv,
    double MonthlyIrr,
    double AnnualIrr,
    double Tcea);

public sealed record SimulationResult(IReadOnlyList<AmortizationEntry> Schedule, SimulationSummary Summary);

/// <summary>Pure calculation entry point: rate conversion, schedule and indicators. No I/O, no framework dependencies.</summary>
public sealed class FinancialEngine
{
    public const double DefaultAnnualDiscountRate = 0.10;

    private readonly double _defaultAnnualDiscountRate;

    public FinancialEngine(double defaultAnnualDiscountRate = DefaultAnnualDiscountRate)
    {
        _defaultAnnualDiscountRate = defaultAnnualDiscountRate;
    }

    public SimulationResult Simulate(LoanSimulationInput input)
    {
        var financedAmount = ValidateAndGetFinancedAmount(input);

        var monthlyRate = RateConverter.ToMonthlyEffectiveRate(input.RateType, input.AnnualRate, input.CapitalizationFrequency);

        var amortization = AmortizationCalculator.BuildSchedule(new AmortizationRequest(
            FinancedAmount: financedAmount,
            MonthlyRate: monthlyRate,
            TermInMonths: input.TermInMonths,
            StartDate: input.StartDate,
            GraceType: input.GraceType,
            GraceMonths: input.GraceMonths,
            BalloonRatio: input.BalloonRatio,
            MonthlyPropertyInsurance: input.MonthlyPropertyInsurance,
            MonthlyCreditLifeInsurance: input.MonthlyCreditLifeInsurance));

        var totals = new List<double>(amortization.Schedule.Count);
        var totalPaid = 0.0;
        var totalInterest = 0.0;
        var insuranceCosts = 0.0;
        foreach (var entry in amortization.Schedule)
        {
            totals.Add(entry.TotalPayment);
            totalPaid += entry.TotalPayment;
            totalInterest += entry.Interest;
            insuranceCosts += entry.PropertyInsurance + entry.CreditLifeInsurance;
        }

        var indicators = FinancialIndicators.Calculate(
            financedAmount,
            totals,
            input.AnnualDiscountRate ?? _defaultAnnualDiscountRate);

        var summary = new SimulationSummary(
            FinancedAmount: financedAmount,
            RegularInstallment: amortization.RegularInstallment,
            BalloonAmount: amortization.BalloonAmount,
            TotalPaid: Rounding.Money(totalPaid),
            TotalInterest: Rounding.Money(totalInterest),
            AdditionalCosts: Rounding.Money(insuranceCosts + input.AdministrativeExpenses),
            Npv: indicators.Npv,
            MonthlyIrr: indicators.MonthlyIrr ?? 0.0,
            AnnualIrr: indicators.AnnualIrr ?? 0.0,
            Tcea: indicators.Tcea ?? 0.0);

        return new SimulationResult(amortization.Schedule, summary);
    }

    private static double ValidateAndGetFinancedAmount(LoanSimulationInput input)
    {
        if (input.PropertyPrice <= 0)
        {
            throw new ArgumentException("The property price must be greater than 0.", nameof(input));
        }

        if (input.DownPayment < 0 || input.DownPayment > input.PropertyPrice)
        {
            throw new ArgumentException("The down payment must be between 0 and the property price.", nameof(input));
        }

        if (input.BenefitAmount < 0 || input.MonthlyPropertyInsurance < 0 || input.MonthlyCreditLifeInsurance < 0 || input.AdministrativeExpenses < 0)
        {
            throw new ArgumentException("Benefit, insurance and administrative amounts cannot be negative.", nameof(input));
        }

        var financedAmount = Rounding.Money(input.PropertyPrice - input.DownPayment - input.BenefitAmount);
        if (financedAmount <= 0)
        {
            throw new ArgumentException("The down payment and the benefit leave nothing to finance.", nameof(input));
        }

        return financedAmount;
    }
}
