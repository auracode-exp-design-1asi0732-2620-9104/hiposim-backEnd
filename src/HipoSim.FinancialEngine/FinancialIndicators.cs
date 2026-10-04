namespace HipoSim.FinancialEngine;

public sealed record FinancialIndicatorResult(double Npv, double? MonthlyIrr, double? AnnualIrr, double? Tcea);

/// <summary>
/// NPV, IRR and TCEA over a cash flow where index 0 is the disbursement and 1..n are the periodic payments.
/// No external dependencies: Newton-Raphson with a bisection fallback.
/// </summary>
public static class FinancialIndicators
{
    /// <summary>NPV = sum(F_t / (1 + k)^t), t = 0..n; cashFlows[0] keeps its own sign.</summary>
    public static double Npv(IReadOnlyList<double> cashFlows, double periodicRate)
    {
        var npv = 0.0;
        for (var t = 0; t < cashFlows.Count; t++)
        {
            npv += cashFlows[t] / Math.Pow(1 + periodicRate, t);
        }

        return npv;
    }

    /// <summary>Rate that makes the NPV zero, or null when no sign change can be bracketed.</summary>
    public static double? Irr(
        IReadOnlyList<double> cashFlows,
        double initialGuess = 0.05,
        double tolerance = 1e-9,
        int maxIterations = 500)
    {
        var rate = initialGuess;
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var npv = Npv(cashFlows, rate);
            var derivative = 0.0;
            for (var t = 1; t < cashFlows.Count; t++)
            {
                derivative += -t * cashFlows[t] / Math.Pow(1 + rate, t + 1);
            }

            if (Math.Abs(derivative) < 1e-12)
            {
                break;
            }

            var nextRate = rate - npv / derivative;
            if (nextRate <= -0.9999)
            {
                nextRate = (rate - 0.9999) / 2;
            }

            if (Math.Abs(nextRate - rate) < tolerance)
            {
                return nextRate;
            }

            rate = nextRate;
        }

        return IrrByBisection(cashFlows);
    }

    private static double? IrrByBisection(
        IReadOnlyList<double> cashFlows,
        double low = -0.9,
        double high = 5.0,
        double tolerance = 1e-7,
        int maxIterations = 200)
    {
        var npvLow = Npv(cashFlows, low);
        var npvHigh = Npv(cashFlows, high);
        if (double.IsNaN(npvLow) || double.IsNaN(npvHigh) || npvLow * npvHigh > 0)
        {
            return null;
        }

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var mid = (low + high) / 2;
            var npvMid = Npv(cashFlows, mid);
            if (Math.Abs(npvMid) < tolerance)
            {
                return mid;
            }

            if (npvLow * npvMid < 0)
            {
                high = mid;
            }
            else
            {
                low = mid;
                npvLow = npvMid;
            }
        }

        return (low + high) / 2;
    }

    /// <summary>
    /// Lender's view: -disbursement at t = 0, then each total payment (insurance included).
    /// The NPV uses the annual discount rate converted to monthly; the TCEA is the annualized IRR
    /// of that real cash flow (SBS Resolution 8181-2012).
    /// </summary>
    public static FinancialIndicatorResult Calculate(
        double financedAmount,
        IReadOnlyList<double> installmentTotals,
        double annualDiscountRate)
    {
        var cashFlows = new double[installmentTotals.Count + 1];
        cashFlows[0] = -financedAmount;
        for (var t = 0; t < installmentTotals.Count; t++)
        {
            cashFlows[t + 1] = installmentTotals[t];
        }

        var monthlyDiscountRate = Math.Pow(1 + annualDiscountRate, 1.0 / 12) - 1;
        var npv = Npv(cashFlows, monthlyDiscountRate);

        var monthlyIrr = Irr(cashFlows, initialGuess: 0.02);
        double? annualIrr = monthlyIrr is null ? null : RateConverter.MonthlyToEffectiveAnnual(monthlyIrr.Value);

        return new FinancialIndicatorResult(
            Npv: Rounding.Money(npv),
            MonthlyIrr: monthlyIrr is null ? null : Rounding.Rate(monthlyIrr.Value),
            AnnualIrr: annualIrr is null ? null : Rounding.Rate(annualIrr.Value),
            Tcea: annualIrr is null ? null : Rounding.Rate(annualIrr.Value));
    }
}
