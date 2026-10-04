namespace HipoSim.FinancialEngine;

/// <summary>
/// Interest rate conversions. All rates are fractions (0.085 = 8.5%), never percentages.
/// Commercial calendar: 360-day year and 30-day month.
/// </summary>
public static class RateConverter
{
    public const int CommercialDaysPerYear = 360;
    public const int CommercialDaysPerMonth = 30;

    /// <summary>TEA = (1 + TNA / m)^m - 1</summary>
    public static double NominalToEffectiveAnnual(double nominalAnnualRate, CapitalizationFrequency frequency)
    {
        var periodsPerYear = (int)frequency;
        return Math.Pow(1 + nominalAnnualRate / periodsPerYear, periodsPerYear) - 1;
    }

    /// <summary>TEP = (1 + TEA)^(days / 360) - 1</summary>
    public static double AnnualToPeriodic(double effectiveAnnualRate, int days = CommercialDaysPerMonth)
    {
        return Math.Pow(1 + effectiveAnnualRate, (double)days / CommercialDaysPerYear) - 1;
    }

    /// <summary>Monthly effective rate for the French method, from the rate as the user typed it.</summary>
    public static double ToMonthlyEffectiveRate(RateType rateType, double annualRate, CapitalizationFrequency? frequency)
    {
        double effectiveAnnualRate;
        switch (rateType)
        {
            case RateType.Nominal:
                if (frequency is null)
                {
                    throw new ArgumentException("A capitalization frequency is required for a nominal rate.", nameof(frequency));
                }

                effectiveAnnualRate = NominalToEffectiveAnnual(annualRate, frequency.Value);
                break;
            case RateType.Effective:
                effectiveAnnualRate = annualRate;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(rateType), rateType, "Unknown rate type.");
        }

        return AnnualToPeriodic(effectiveAnnualRate, CommercialDaysPerMonth);
    }

    /// <summary>Inverse of <see cref="AnnualToPeriodic"/> for a monthly rate: (1 + i)^12 - 1.</summary>
    public static double MonthlyToEffectiveAnnual(double monthlyRate) => Math.Pow(1 + monthlyRate, 12) - 1;
}
