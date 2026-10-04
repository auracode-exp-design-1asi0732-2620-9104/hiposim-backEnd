namespace HipoSim.Simulations;

/// <summary>
/// Body of <c>POST /api/simulations</c> (openapi.v0.yaml). Everything is nullable so that a missing field is
/// reported by <see cref="SimulationRequestValidator"/> with the same message format as the other rules.
/// </summary>
public sealed record SimulationRequest
{
    public double? PropertyPrice { get; init; }

    public double? DownPayment { get; init; }

    /// <summary>Effective annual rate as a fraction: 0.085 means 8.5%.</summary>
    public double? AnnualEffectiveRate { get; init; }

    public int? TermInMonths { get; init; }

    public int? GracePeriodMonths { get; init; }

    /// <summary>"none", "total" or "partial" (case-insensitive).</summary>
    public string? GraceType { get; init; }

    public bool? ApplyGoodPayerBonus { get; init; }

    public DateOnly? StartDate { get; init; }
}

public sealed record SimulationResponse(
    Guid SimulationId,
    double FinancedAmount,
    double MonthlyInstallment,
    double TotalPaid,
    double TotalInterest,
    double AdditionalCosts,
    double Tcea,
    double Irr,
    double Npv,
    BenefitResponse Benefit,
    AssumptionsResponse Assumptions,
    IReadOnlyList<AmortizationEntryResponse> AmortizationSchedule);

public sealed record BenefitResponse(string Name, bool Eligible, double AppliedAmount);

public sealed record AssumptionsResponse(
    double MonthlyPropertyInsurance,
    double MonthlyCreditLifeInsurance,
    double AdministrativeExpenses,
    double AnnualDiscountRate);

public sealed record AmortizationEntryResponse(
    int Period,
    DateOnly PaymentDate,
    double OpeningBalance,
    double Installment,
    double Interest,
    double Amortization,
    double RemainingBalance,
    double PropertyInsurance,
    double CreditLifeInsurance,
    double TotalPayment,
    bool IsGracePeriod);
