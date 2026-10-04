namespace HipoSim.FinancialEngine;

/// <summary>
/// Single place for the engine's rounding rules (same as the Python original: banker's rounding,
/// 2 decimals for money and 6 for rates, applied only to the values that leave the engine).
/// </summary>
internal static class Rounding
{
    // "+ 0.0" turns a negative zero (e.g. -1e-12 rounded) into +0.0 so it never shows up as "-0" in JSON.
    public static double Money(double value) => Math.Round(value, 2, MidpointRounding.ToEven) + 0.0;

    public static double Rate(double value) => Math.Round(value, 6, MidpointRounding.ToEven) + 0.0;
}
