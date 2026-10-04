namespace HipoSim.FinancialEngine;

public enum RateType
{
    Effective,
    Nominal,
}

public enum GraceType
{
    None,
    Total,
    Partial,
}

/// <summary>Capitalization periods per year; the numeric value is the divisor used by the nominal-to-effective conversion.</summary>
public enum CapitalizationFrequency
{
    Daily = 360,
    Monthly = 12,
    Bimonthly = 6,
    Quarterly = 4,
    FourMonthly = 3,
    Semiannual = 2,
    Annual = 1,
}
