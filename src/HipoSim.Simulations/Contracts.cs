namespace HipoSim.Simulations;

/// <summary>
/// Body of <c>POST /api/simulations</c> (openapi.v0.yaml). Everything is nullable so that a missing field is
/// reported by <see cref="SimulationRequestValidator"/> with the same message format as the other rules.
/// </summary>
public sealed record SimulationRequest
{
    /// <summary>Property price in PEN. Required, greater than 0.</summary>
    public double? PropertyPrice { get; init; }

    /// <summary>Down payment in PEN. Required, at least 10% of the property price and at most the price.</summary>
    public double? DownPayment { get; init; }

    /// <summary>Effective annual rate as a fraction: 0.085 means 8.5%. Required, greater than 0 and at most 1.</summary>
    public double? AnnualEffectiveRate { get; init; }

    /// <summary>Loan term in months. Required, from 60 to 360.</summary>
    public int? TermInMonths { get; init; }

    /// <summary>Months of grace at the start of the loan. Defaults to 0 and must be lower than the term.</summary>
    public int? GracePeriodMonths { get; init; }

    /// <summary>"none", "total" or "partial" (case-insensitive). Defaults to "none".</summary>
    public string? GraceType { get; init; }

    /// <summary>Whether the Good Payer Bonus is requested. Defaults to false.</summary>
    public bool? ApplyGoodPayerBonus { get; init; }

    /// <summary>Loan start date (ISO 8601). Defaults to today in America/Lima; the first payment is one month later.</summary>
    public DateOnly? StartDate { get; init; }
}

/// <summary>Result of a mortgage simulation.</summary>
/// <param name="SimulationId">Identifier of the persisted simulation. A lead can reference it.</param>
/// <param name="FinancedAmount">propertyPrice - downPayment - benefit.appliedAmount.</param>
/// <param name="MonthlyInstallment">Regular monthly installment (capital plus interest), without insurance.</param>
/// <param name="TotalPaid">Sum of the totalPayment of every schedule row.</param>
/// <param name="TotalInterest">Total interest paid during the term.</param>
/// <param name="AdditionalCosts">Insurance paid during the whole term plus administrative expenses.</param>
/// <param name="Tcea">Annual effective cost rate (fraction with 6 decimals), insurance included.</param>
/// <param name="Irr">Annual IRR of the same cash flow. Equal to tcea in this version.</param>
/// <param name="Npv">Net present value from the lender's side, discounted at assumptions.annualDiscountRate.</param>
/// <param name="Benefit">Result of the Good Payer Bonus evaluation.</param>
/// <param name="Assumptions">Server-side values used in the calculation.</param>
/// <param name="AmortizationSchedule">One row per installment, grace months included.</param>
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

/// <summary>Good Payer Bonus evaluation.</summary>
/// <param name="Name">Benefit name.</param>
/// <param name="Eligible">The property price is inside a bonus range, even when the bonus was not requested.</param>
/// <param name="AppliedAmount">Amount subtracted from the financed amount. 0 when not requested or not eligible.</param>
public sealed record BenefitResponse(string Name, bool Eligible, double AppliedAmount);

/// <summary>Server-side values used in the calculation, so the apps can show them.</summary>
/// <param name="MonthlyPropertyInsurance">Monthly property insurance in PEN.</param>
/// <param name="MonthlyCreditLifeInsurance">Monthly credit life insurance in PEN.</param>
/// <param name="AdministrativeExpenses">Administrative expenses in PEN.</param>
/// <param name="AnnualDiscountRate">Annual rate used to discount the NPV.</param>
public sealed record AssumptionsResponse(
    double MonthlyPropertyInsurance,
    double MonthlyCreditLifeInsurance,
    double AdministrativeExpenses,
    double AnnualDiscountRate);

/// <summary>One row of the amortization schedule (US02).</summary>
/// <param name="Period">Installment number, starting at 1 (grace months included).</param>
/// <param name="PaymentDate">Payment date (ISO 8601).</param>
/// <param name="OpeningBalance">Balance at the start of the period.</param>
/// <param name="Installment">Capital plus interest paid in the period, without insurance. 0 in a total grace month.</param>
/// <param name="Interest">Interest of the period.</param>
/// <param name="Amortization">Capital repaid in the period.</param>
/// <param name="RemainingBalance">Balance at the end of the period.</param>
/// <param name="PropertyInsurance">Property insurance of the period.</param>
/// <param name="CreditLifeInsurance">Credit life insurance of the period.</param>
/// <param name="TotalPayment">Installment plus insurance.</param>
/// <param name="IsGracePeriod">The period belongs to the grace period.</param>
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