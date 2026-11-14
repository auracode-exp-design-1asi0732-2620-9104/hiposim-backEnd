using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using HipoSim.FinancialEngine;

namespace HipoSim.Simulations.Tests;

/// <summary>
/// US03 against the real controller (TestServer): partial and total grace, with the JSON a client would send.
/// The expected figures come from closed formulas written in this class (compound monthly rate, French installment,
/// capitalization), not from engine code, so a mistake in the engine cannot hide behind itself.
/// Example: S/ 280,000, S/ 42,000 down payment, bonus of S/ 18,200 (financed S/ 219,800), TEA 8.85%, 240 months.
/// </summary>
public class GracePeriodEndpointTests
{
    private const double Tea = 0.0885;
    private const double Financed = 219_800;
    private const int Term = 240;
    private const int Grace = 3;

    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static double MonthlyRate => Math.Pow(1 + Tea, 30.0 / 360) - 1;

    private static double Money(double value) => Math.Round(value, 2, MidpointRounding.ToEven);

    private static double French(double capital, int months) =>
        capital * MonthlyRate / (1 - Math.Pow(1 + MonthlyRate, -months));

    private static string Request(string? graceType, int? graceMonths, int term = Term) =>
        JsonSerializer.Serialize(
            new
            {
                propertyPrice = 280_000,
                downPayment = 42_000,
                annualEffectiveRate = Tea,
                termInMonths = term,
                gracePeriodMonths = graceMonths,
                graceType,
                applyGoodPayerBonus = true,
                startDate = "2026-10-01",
            },
            RequestOptions);

    private static async Task<JsonElement> PostOkAsync(SimulationsTestHost host, string json)
    {
        var response = await host.PostAsync(json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static double Number(JsonElement row, string name) => row.GetProperty(name).GetDouble();

    private static JsonElement Last(JsonElement schedule) => schedule[schedule.GetArrayLength() - 1];

    [Fact]
    public async Task Post_WhenPartialGraceOfThreeMonths_PaysOnlyInterestThenRecalculatesTheInstallmentFromMonthFour()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var body = await PostOkAsync(host, Request("partial", Grace));

        var schedule = body.GetProperty("amortizationSchedule");
        Assert.Equal(Term, schedule.GetArrayLength());
        var interest = Money(Financed * MonthlyRate);
        for (var month = 1; month <= Grace; month++)
        {
            var row = schedule[month - 1];
            Assert.Equal(month, row.GetProperty("period").GetInt32());
            Assert.True(row.GetProperty("isGracePeriod").GetBoolean());
            Assert.Equal(interest, Number(row, "installment"), 2);
            Assert.Equal(interest, Number(row, "interest"), 2);
            Assert.Equal(0, Number(row, "amortization"));
            Assert.Equal(Financed, Number(row, "openingBalance"));
            Assert.Equal(Financed, Number(row, "remainingBalance"));
            Assert.Equal(interest, Number(row, "totalPayment"), 2);
        }

        var regular = Money(French(Financed, Term - Grace));
        var fourth = schedule[Grace];
        Assert.Equal(Grace + 1, fourth.GetProperty("period").GetInt32());
        Assert.False(fourth.GetProperty("isGracePeriod").GetBoolean());
        Assert.Equal(Financed, Number(fourth, "openingBalance"));
        Assert.Equal(regular, Number(fourth, "installment"), 2);
        Assert.Equal(regular, Number(body, "monthlyInstallment"), 2);
        Assert.InRange(Number(fourth, "amortization"), regular - interest - 0.011, regular - interest + 0.011);
        Assert.True(regular > Money(French(Financed, Term)), "The installment is recalculated over fewer months.");
        Assert.Equal(Term, Last(schedule).GetProperty("period").GetInt32());
        Assert.Equal(0, Number(Last(schedule), "remainingBalance"));
    }

    [Fact]
    public async Task Post_WhenTotalGraceOfThreeMonths_RecordsNoPaymentsAndCapitalizesTheInterest()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var body = await PostOkAsync(host, Request("total", Grace));

        var schedule = body.GetProperty("amortizationSchedule");
        Assert.Equal(Term, schedule.GetArrayLength());
        var growth = 1 + MonthlyRate;
        for (var month = 1; month <= Grace; month++)
        {
            var row = schedule[month - 1];
            Assert.Equal(month, row.GetProperty("period").GetInt32());
            Assert.True(row.GetProperty("isGracePeriod").GetBoolean());
            Assert.Equal(0, Number(row, "installment"));
            Assert.Equal(0, Number(row, "totalPayment"));
            Assert.Equal(0, Number(row, "amortization"));
            Assert.Equal(Money(Financed * Math.Pow(growth, month - 1)), Number(row, "openingBalance"), 2);
            Assert.Equal(Money(Financed * Math.Pow(growth, month - 1) * MonthlyRate), Number(row, "interest"), 2);
            Assert.Equal(Money(Financed * Math.Pow(growth, month)), Number(row, "remainingBalance"), 2);
        }

        var capitalized = Financed * Math.Pow(growth, Grace);
        var regular = Money(French(capitalized, Term - Grace));
        Assert.Equal(regular, Number(body, "monthlyInstallment"), 2);
        var fourth = schedule[Grace];
        Assert.False(fourth.GetProperty("isGracePeriod").GetBoolean());
        Assert.Equal(Money(capitalized), Number(fourth, "openingBalance"), 2);
        Assert.Equal(regular, Number(fourth, "installment"), 2);
        Assert.True(regular > Money(French(Financed, Term - Grace)), "The capitalized interest is repaid in the installment.");
    }

    [Fact]
    public async Task Post_WhenTotalGrace_EveryRegularInstallmentIsEqualAndTheLastOneIsNotAHugeBalloon()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var body = await PostOkAsync(host, Request("total", Grace));

        var schedule = body.GetProperty("amortizationSchedule");
        var regular = Number(body, "monthlyInstallment");
        for (var index = Grace; index < Term; index++)
        {
            // The last row only absorbs the cents left by rounding.
            Assert.InRange(Number(schedule[index], "installment"), regular - 0.10, regular + 0.10);
        }

        Assert.Equal(0, Number(Last(schedule), "remainingBalance"));
        var balances = Enumerable.Range(Grace, Term - Grace).Select(index => Number(schedule[index], "remainingBalance")).ToArray();
        Assert.Equal(balances.OrderByDescending(balance => balance), balances);
    }

    [Fact]
    public async Task Post_WhenGraceIsAdded_TheCostGrowsFromNoGraceToPartialToTotal()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var none = await PostOkAsync(host, Request(null, null));
        var partial = await PostOkAsync(host, Request("partial", Grace));
        var total = await PostOkAsync(host, Request("total", Grace));

        Assert.All(new[] { none, partial, total }, body => Assert.Equal(Financed, Number(body, "financedAmount")));
        Assert.True(Number(none, "totalPaid") < Number(partial, "totalPaid"));
        Assert.True(Number(partial, "totalPaid") < Number(total, "totalPaid"));
        Assert.True(Number(none, "totalInterest") < Number(partial, "totalInterest"));
        Assert.True(Number(partial, "totalInterest") < Number(total, "totalInterest"));

        var partialInterest = Money(Financed * MonthlyRate);
        var partialRegular = Money(French(Financed, Term - Grace));
        Assert.InRange(
            Number(partial, "totalPaid"),
            (Grace * partialInterest) + ((Term - Grace) * partialRegular) - 1,
            (Grace * partialInterest) + ((Term - Grace) * partialRegular) + 1);
        var totalRegular = Number(total, "monthlyInstallment");
        Assert.InRange(Number(total, "totalPaid"), ((Term - Grace) * totalRegular) - 1, ((Term - Grace) * totalRegular) + 1);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("total")]
    public async Task Post_WhenGraceWithoutInsurance_TheTceaEqualsTheRateBecauseTheLoanIsRepaidAtThatRate(string graceType)
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var body = await PostOkAsync(host, Request(graceType, Grace));

        Assert.Equal(Tea, Number(body, "tcea"), 5);
        Assert.Equal(Tea, Number(body, "irr"), 5);
    }

    [Fact]
    public async Task Post_WhenInsuranceIsConfigured_ItIsStillChargedDuringTheGraceMonths()
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Simulation:MonthlyPropertyInsurance"] = "20",
            ["Simulation:MonthlyCreditLifeInsurance"] = "35",
        };
        await using var host = await SimulationsTestHost.StartAsync(configuration);

        var total = await PostOkAsync(host, Request("total", Grace));
        var partial = await PostOkAsync(host, Request("partial", Grace));

        var totalGraceRow = total.GetProperty("amortizationSchedule")[0];
        Assert.Equal(0, Number(totalGraceRow, "installment"));
        Assert.Equal(55, Number(totalGraceRow, "totalPayment"));
        var partialGraceRow = partial.GetProperty("amortizationSchedule")[0];
        Assert.Equal(Money(Financed * MonthlyRate) + 55, Number(partialGraceRow, "totalPayment"), 2);
        Assert.True(Number(total, "tcea") > Tea, "Insurance raises the TCEA above the rate.");
        Assert.Equal(Term * 55, Number(total, "additionalCosts"), 2);
    }

    [Fact]
    public async Task Post_WhenGraceTypeIsWrittenInAnyCase_IsAcceptedAndTheGraceMonthsAreMarked()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var body = await PostOkAsync(host, Request("PARTIAL", Grace));

        var schedule = body.GetProperty("amortizationSchedule");
        Assert.Equal(Grace, schedule.EnumerateArray().Count(row => row.GetProperty("isGracePeriod").GetBoolean()));
    }

    [Theory]
    [InlineData("partial", 239)]
    [InlineData("total", 239)]
    public async Task Post_WhenGraceLeavesASingleRegularInstallment_StillEndsAtZero(string graceType, int graceMonths)
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var body = await PostOkAsync(host, Request(graceType, graceMonths));

        var schedule = body.GetProperty("amortizationSchedule");
        Assert.Equal(Term, schedule.GetArrayLength());
        Assert.True(schedule[graceMonths - 1].GetProperty("isGracePeriod").GetBoolean());
        Assert.False(Last(schedule).GetProperty("isGracePeriod").GetBoolean());
        Assert.Equal(0, Number(Last(schedule), "remainingBalance"));
    }

    [Theory]
    [InlineData("partial", null, "gracePeriodMonths", "The gracePeriodMonths must be greater than 0 when graceType is 'total' or 'partial'.")]
    [InlineData(null, 6, "graceType", "The graceType must be 'total' or 'partial' when gracePeriodMonths is greater than 0.")]
    [InlineData("sometimes", 0, "graceType", "The graceType must be one of: none, total, partial.")]
    [InlineData("total", 240, "gracePeriodMonths", "The gracePeriodMonths must be lower than the termInMonths.")]
    [InlineData("partial", -1, "gracePeriodMonths", "The gracePeriodMonths cannot be negative.")]
    public async Task Post_WhenTheGraceIsInconsistent_Returns400KeyedByTheFieldAndStoresNothing(
        string? graceType,
        int? graceMonths,
        string expectedField,
        string expectedMessage)
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.PostAsync(Request(graceType, graceMonths));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal(
            [expectedMessage],
            problem.GetProperty("errors").GetProperty(expectedField).EnumerateArray().Select(message => message.GetString()!).ToArray());
        Assert.Empty(host.Repository.Records);
    }

    [Theory]
    [InlineData("partial", GraceType.Partial)]
    [InlineData("total", GraceType.Total)]
    public async Task Post_WhenGraceIsRequested_TheStoredSimulationKeepsTheGraceInputAndTheMarkedSchedule(
        string graceType,
        GraceType expected)
    {
        await using var host = await SimulationsTestHost.StartAsync();

        await PostOkAsync(host, Request(graceType, Grace));

        var record = Assert.Single(host.Repository.Records);
        Assert.Equal(expected, record.Input.GraceType);
        Assert.Equal(Grace, record.Input.GraceMonths);
        Assert.Equal(Grace, record.Response.AmortizationSchedule.Count(entry => entry.IsGracePeriod));
    }
}
