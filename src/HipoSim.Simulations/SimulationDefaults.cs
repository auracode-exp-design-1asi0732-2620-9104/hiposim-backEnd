using HipoSim.FinancialEngine;

namespace HipoSim.Simulations;

/// <summary>
/// Server-side assumptions that the request does not carry (open question 3 of the contract).
/// Bound from the "Simulation" configuration section; they are echoed in the response as <c>assumptions</c>.
/// </summary>
public sealed class SimulationDefaults
{
    public const string SectionName = "Simulation";

    public double MonthlyPropertyInsurance { get; set; }

    public double MonthlyCreditLifeInsurance { get; set; }

    public double AdministrativeExpenses { get; set; }

    public double AnnualDiscountRate { get; set; } = FinancialEngine.FinancialEngine.DefaultAnnualDiscountRate;
}
