using HipoSim.FinancialEngine;

namespace HipoSim.FinancialEngine.Tests;

public class FinancialEngineTests
{
    private readonly FinancialEngine _engine = new();

    private static LoanSimulationInput ContractExample(double benefitAmount = 18_200) => new(
        PropertyPrice: 280_000,
        DownPayment: 42_000,
        RateType: RateType.Effective,
        AnnualRate: 0.085,
        TermInMonths: 240,
        StartDate: new DateOnly(2026, 10, 1),
        BenefitAmount: benefitAmount);

    [Fact]
    public void Simulate_WhenContractExampleWithBonus_FinancesPriceMinusDownPaymentMinusBonus()
    {
        var result = _engine.Simulate(ContractExample());

        Assert.Equal(219_800, result.Summary.FinancedAmount);
        Assert.Equal(240, result.Schedule.Count);
        Assert.Equal(0.0, result.Schedule[^1].RemainingBalance, 2);
    }

    [Fact]
    public void Simulate_WhenBonusIsApplied_InstallmentIsLowerThanWithoutIt()
    {
        var withBonus = _engine.Simulate(ContractExample(benefitAmount: 18_200));
        var withoutBonus = _engine.Simulate(ContractExample(benefitAmount: 0));

        Assert.True(withBonus.Summary.RegularInstallment < withoutBonus.Summary.RegularInstallment);
        Assert.Equal(238_000, withoutBonus.Summary.FinancedAmount);
    }

    [Fact]
    public void Simulate_WhenNoInsurance_TceaMatchesTheEffectiveRate()
    {
        var result = _engine.Simulate(ContractExample());

        Assert.Equal(0.085, result.Summary.Tcea, 4);
    }

    [Fact]
    public void Simulate_WhenDiscountRateIsNotGiven_UsesTheEngineDefault()
    {
        var withDefault = new FinancialEngine().Simulate(ContractExample());
        var explicitTenPercent = new FinancialEngine().Simulate(ContractExample() with { AnnualDiscountRate = 0.10 });
        var custom = new FinancialEngine(defaultAnnualDiscountRate: 0.05).Simulate(ContractExample());

        Assert.Equal(explicitTenPercent.Summary.Npv, withDefault.Summary.Npv);
        Assert.NotEqual(withDefault.Summary.Npv, custom.Summary.Npv);
    }

    [Fact]
    public void Simulate_WhenAdministrativeExpensesAreGiven_TheyCountOnceInAdditionalCosts()
    {
        var input = ContractExample() with
        {
            MonthlyPropertyInsurance = 10,
            MonthlyCreditLifeInsurance = 20,
            AdministrativeExpenses = 300,
        };

        var result = _engine.Simulate(input);

        Assert.Equal(240 * 30 + 300, result.Summary.AdditionalCosts, 2);
    }

    [Fact]
    public void Simulate_WhenNominalRate_ConvertsThroughTheCapitalizationFrequency()
    {
        var nominal = ContractExample() with
        {
            RateType = RateType.Nominal,
            AnnualRate = 0.12,
            CapitalizationFrequency = CapitalizationFrequency.Monthly,
        };

        var result = _engine.Simulate(nominal);

        // 12% nominal with monthly capitalization is 12.6825% effective annual.
        Assert.Equal(0.126825, result.Summary.Tcea, 4);
    }

    [Fact]
    public void Simulate_WhenGraceIsTotal_TheGraceRowsHaveNoInstallment()
    {
        var input = ContractExample() with { GraceType = GraceType.Total, GraceMonths = 6 };

        var result = _engine.Simulate(input);

        Assert.All(result.Schedule.Take(6), entry =>
        {
            Assert.True(entry.IsGracePeriod);
            Assert.Equal(0.0, entry.Installment);
        });
        Assert.Equal(240, result.Schedule.Count);
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void Simulate_WhenInputIsInvalid_Throws(LoanSimulationInput input)
    {
        Assert.Throws<ArgumentException>(() => _engine.Simulate(input));
    }

    public static TheoryData<LoanSimulationInput> InvalidInputs() => new()
    {
        ContractExample() with { PropertyPrice = 0 },
        ContractExample() with { DownPayment = -1 },
        ContractExample() with { DownPayment = 300_000 },
        ContractExample() with { BenefitAmount = 238_000 },
        ContractExample() with { AdministrativeExpenses = -5 },
        ContractExample() with { RateType = RateType.Nominal, CapitalizationFrequency = null },
    };
}
