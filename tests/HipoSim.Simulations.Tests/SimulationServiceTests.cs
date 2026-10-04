using HipoSim.FinancialEngine;
using HipoSim.Simulations;
using Microsoft.Extensions.Options;

namespace HipoSim.Simulations.Tests;

public class SimulationServiceTests
{
    private readonly RecordingRepository _repository = new();

    private static SimulationRequest ContractExample() => new()
    {
        PropertyPrice = 280_000,
        DownPayment = 42_000,
        AnnualEffectiveRate = 0.085,
        TermInMonths = 240,
        ApplyGoodPayerBonus = true,
        StartDate = new DateOnly(2026, 10, 1),
    };

    private SimulationService CreateService(SimulationDefaults? defaults = null, DateTimeOffset? now = null) => new(
        new SimulationRequestValidator(),
        new BenefitsEngine(),
        new FinancialEngine.FinancialEngine(),
        Options.Create(defaults ?? new SimulationDefaults()),
        _repository,
        new FakeTimeProvider(now ?? new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task CalculateAsync_WhenContractExampleWithBonus_ReturnsTheDocumentedFigures()
    {
        var response = await CreateService().CalculateAsync(ContractExample(), buyerId: null);

        Assert.Equal(219_800, response.FinancedAmount);
        Assert.Equal(1863.99, response.MonthlyInstallment);
        Assert.Equal(0.085, response.Tcea, 6);
        Assert.Equal(response.Tcea, response.Irr);
        Assert.Equal(new BenefitResponse("Good Payer Bonus", true, 18_200), response.Benefit);
        Assert.Equal(240, response.AmortizationSchedule.Count);
        Assert.Equal(new DateOnly(2026, 11, 1), response.AmortizationSchedule[0].PaymentDate);
    }

    [Fact]
    public async Task CalculateAsync_WhenCalculated_EachRowOpensWithThePreviousClosingBalance()
    {
        var request = ContractExample() with { GraceType = "total", GracePeriodMonths = 6 };

        var response = await CreateService().CalculateAsync(request, buyerId: null);

        var schedule = response.AmortizationSchedule;
        Assert.Equal(response.FinancedAmount, schedule[0].OpeningBalance);
        for (var index = 1; index < schedule.Count; index++)
        {
            Assert.Equal(schedule[index - 1].RemainingBalance, schedule[index].OpeningBalance);
        }

        // With total grace the balance grows, so the opening balance of the first regular row is above the financed amount.
        Assert.True(schedule[6].OpeningBalance > response.FinancedAmount);
    }

    [Fact]
    public async Task CalculateAsync_WhenBonusIsNotRequested_ReportsEligibilityButFinancesTheFullAmount()
    {
        var request = ContractExample() with { ApplyGoodPayerBonus = false };

        var response = await CreateService().CalculateAsync(request, buyerId: null);

        Assert.Equal(238_000, response.FinancedAmount);
        Assert.True(response.Benefit.Eligible);
        Assert.Equal(0, response.Benefit.AppliedAmount);
    }

    [Fact]
    public async Task CalculateAsync_WhenPropertyIsOutsideTheBonusRange_DoesNotChangeTheFinancedAmount()
    {
        var request = ContractExample() with { PropertyPrice = 450_000, DownPayment = 90_000 };

        var response = await CreateService().CalculateAsync(request, buyerId: null);

        Assert.Equal(360_000, response.FinancedAmount);
        Assert.False(response.Benefit.Eligible);
        Assert.Equal(0, response.Benefit.AppliedAmount);
    }

    [Fact]
    public async Task CalculateAsync_WhenDefaultsHaveInsurance_ThePaymentsIncludeItAndTheTceaRises()
    {
        var defaults = new SimulationDefaults
        {
            MonthlyPropertyInsurance = 20,
            MonthlyCreditLifeInsurance = 35,
            AdministrativeExpenses = 300,
            AnnualDiscountRate = 0.12,
        };

        var response = await CreateService(defaults).CalculateAsync(ContractExample(), buyerId: null);

        Assert.Equal(new AssumptionsResponse(20, 35, 300, 0.12), response.Assumptions);
        Assert.Equal(1863.99 + 55, response.AmortizationSchedule[0].TotalPayment, 2);
        Assert.Equal(240 * 55 + 300, response.AdditionalCosts, 2);
        Assert.True(response.Tcea > 0.085);
    }

    [Fact]
    public async Task CalculateAsync_WhenNoStartDate_UsesTodayInLimaNotInUtc()
    {
        // 03:00 UTC on Oct 2 is still 22:00 on Oct 1 in Lima (UTC-5).
        var now = new DateTimeOffset(2026, 10, 2, 3, 0, 0, TimeSpan.Zero);
        var request = ContractExample() with { StartDate = null };

        var response = await CreateService(now: now).CalculateAsync(request, buyerId: null);

        Assert.Equal(new DateOnly(2026, 11, 1), response.AmortizationSchedule[0].PaymentDate);
    }

    [Fact]
    public async Task CalculateAsync_WhenSuccessful_PersistsOneRecordWithTheBuyerAndTheResponseId()
    {
        var buyerId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        var response = await CreateService(now: now).CalculateAsync(ContractExample(), buyerId);

        var record = Assert.Single(_repository.Records);
        Assert.Equal(response.SimulationId, record.Id);
        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal(buyerId, record.BuyerId);
        Assert.Equal(now, record.CreatedAt);
        Assert.Equal(18_200, record.Input.BenefitAmount);
        Assert.Same(response, record.Response);
    }

    [Fact]
    public async Task CalculateAsync_WhenRequestIsInvalid_ThrowsWithTheFieldsAndPersistsNothing()
    {
        var request = ContractExample() with { DownPayment = 1_000 };

        var exception = await Assert.ThrowsAsync<SimulationValidationException>(
            () => CreateService().CalculateAsync(request, buyerId: null));

        Assert.Contains("downPayment", exception.Errors.Keys);
        Assert.Empty(_repository.Records);
    }

    [Fact]
    public async Task CalculateAsync_WhenDownPaymentAndBonusLeaveNothingToFinance_ThrowsAValidationError()
    {
        // 120,000 with a 90% down payment (108,000) plus the 26,400 bonus is more than the price.
        var request = ContractExample() with { PropertyPrice = 120_000, DownPayment = 108_000 };

        var exception = await Assert.ThrowsAsync<SimulationValidationException>(
            () => CreateService().CalculateAsync(request, buyerId: null));

        Assert.Equal(["The down payment and the bonus leave nothing to finance."], exception.Errors["downPayment"]);
        Assert.Empty(_repository.Records);
    }

    [Fact]
    public async Task CalculateAsync_WhenPartialGrace_TheFirstMonthsAreMarkedAndTheLoanStillEndsAtZero()
    {
        var request = ContractExample() with { GraceType = "partial", GracePeriodMonths = 6 };

        var response = await CreateService().CalculateAsync(request, buyerId: null);

        Assert.All(response.AmortizationSchedule.Take(6), entry => Assert.True(entry.IsGracePeriod));
        Assert.False(response.AmortizationSchedule[6].IsGracePeriod);
        Assert.Equal(0.0, response.AmortizationSchedule[^1].RemainingBalance, 2);
    }
}
