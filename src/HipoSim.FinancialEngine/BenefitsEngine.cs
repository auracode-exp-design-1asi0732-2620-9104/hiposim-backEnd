namespace HipoSim.FinancialEngine;

/// <summary>One step of the Good Payer Bonus table: properties up to <paramref name="MaxPropertyPrice"/> get <paramref name="Amount"/>.</summary>
public sealed record GoodPayerBonusTier(double MaxPropertyPrice, double Amount);

public sealed record BenefitResult(string Name, bool Eligible, double AppliedAmount);

/// <summary>
/// Evaluates state benefits. The Good Payer Bonus (BBP) is a fixed amount by property price range.
/// </summary>
public sealed class BenefitsEngine
{
    public const string GoodPayerBonusName = "Good Payer Bonus";

    /// <summary>
    /// Reference table taken from the HipoSim Landing Page simulator (js/main.js). It is NOT an official
    /// Fondo Mivivienda table: the amounts and ranges must be confirmed and kept up to date (AdminParameter).
    /// </summary>
    public static IReadOnlyList<GoodPayerBonusTier> ReferenceTiers { get; } =
    [
        new GoodPayerBonusTier(MaxPropertyPrice: 200_000, Amount: 26_400),
        new GoodPayerBonusTier(MaxPropertyPrice: 300_000, Amount: 18_200),
        new GoodPayerBonusTier(MaxPropertyPrice: 400_000, Amount: 10_000),
    ];

    private readonly IReadOnlyList<GoodPayerBonusTier> _tiers;

    public BenefitsEngine(IEnumerable<GoodPayerBonusTier>? tiers = null)
    {
        _tiers = (tiers ?? ReferenceTiers).OrderBy(tier => tier.MaxPropertyPrice).ToList();
    }

    /// <summary>
    /// Eligible means the property price falls inside the bonus range; the amount is only applied
    /// when the buyer asked for it, so the UI can tell a buyer that they qualify before applying.
    /// </summary>
    public BenefitResult EvaluateGoodPayerBonus(double propertyPrice, bool apply)
    {
        var tier = _tiers.FirstOrDefault(candidate => propertyPrice <= candidate.MaxPropertyPrice);
        if (tier is null)
        {
            return new BenefitResult(GoodPayerBonusName, Eligible: false, AppliedAmount: 0);
        }

        return new BenefitResult(GoodPayerBonusName, Eligible: true, AppliedAmount: apply ? tier.Amount : 0);
    }
}
