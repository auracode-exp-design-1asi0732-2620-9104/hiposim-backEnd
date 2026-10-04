using System.Globalization;
using HipoSim.FinancialEngine;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// Contract example: S/ 280,000 property, 15% down payment, 8.5% TEA, 20 years, Good Payer Bonus requested.
const double propertyPrice = 280_000;
var bonus = new BenefitsEngine().EvaluateGoodPayerBonus(propertyPrice, apply: true);

var result = new FinancialEngine().Simulate(new LoanSimulationInput(
    PropertyPrice: propertyPrice,
    DownPayment: 42_000,
    RateType: RateType.Effective,
    AnnualRate: 0.085,
    TermInMonths: 240,
    StartDate: new DateOnly(2026, 10, 1),
    BenefitAmount: bonus.AppliedAmount));

var summary = result.Summary;
Console.WriteLine($"{bonus.Name}: eligible={bonus.Eligible}, applied={bonus.AppliedAmount:N2}");
Console.WriteLine($"Financed amount     {summary.FinancedAmount,14:N2}");
Console.WriteLine($"Monthly installment {summary.RegularInstallment,14:N2}");
Console.WriteLine($"Total paid          {summary.TotalPaid,14:N2}");
Console.WriteLine($"Total interest      {summary.TotalInterest,14:N2}");
Console.WriteLine($"TCEA                {summary.Tcea,14:P4}");
Console.WriteLine($"NPV (10% discount)  {summary.Npv,14:N2}");
Console.WriteLine();
Console.WriteLine("Period  Payment date  Installment    Interest  Amortization       Balance");
foreach (var entry in result.Schedule.Take(3).Append(result.Schedule[^1]))
{
    Console.WriteLine(
        $"{entry.Period,6}  {entry.PaymentDate:yyyy-MM-dd}  {entry.Installment,11:N2}  {entry.Interest,10:N2}  {entry.Amortization,12:N2}  {entry.RemainingBalance,12:N2}");
}
