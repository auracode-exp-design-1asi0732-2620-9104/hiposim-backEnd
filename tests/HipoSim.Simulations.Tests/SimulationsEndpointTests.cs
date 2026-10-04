using System.Text.Json;

namespace HipoSim.Simulations.Tests;

/// <summary>TS06 scenarios against the real controller (TestServer), with the JSON a client would send.</summary>
public class SimulationsEndpointTests
{
    private const string ContractExampleJson = """
        {"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":0.085,"termInMonths":240,
         "gracePeriodMonths":0,"graceType":"none","applyGoodPayerBonus":true,"startDate":"2026-10-01"}
        """;

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string[] ErrorFields(JsonElement problem) =>
        problem.GetProperty("errors").EnumerateObject().Select(property => property.Name).Order().ToArray();

    [Fact]
    public async Task Post_WhenRequestIsValid_Returns200WithTheCalculation()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.PostAsync(ContractExampleJson);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.Equal(219_800, body.GetProperty("financedAmount").GetDouble());
        Assert.Equal(1863.99, body.GetProperty("monthlyInstallment").GetDouble());
        Assert.Equal(0.085, body.GetProperty("tcea").GetDouble(), 6);
        Assert.True(body.GetProperty("benefit").GetProperty("eligible").GetBoolean());
        Assert.Equal(18_200, body.GetProperty("benefit").GetProperty("appliedAmount").GetDouble());
        Assert.Equal(240, body.GetProperty("amortizationSchedule").GetArrayLength());
        Assert.Equal("2026-11-01", body.GetProperty("amortizationSchedule")[0].GetProperty("paymentDate").GetString());
        Assert.True(Guid.TryParse(body.GetProperty("simulationId").GetString(), out _));
    }

    [Fact]
    public async Task Post_WhenDownPaymentIsBelowTenPercent_Returns400AsProblemJson()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.PostAsync(
            """{"propertyPrice":280000,"downPayment":27999,"annualEffectiveRate":0.085,"termInMonths":240}""");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("One or more validation errors occurred.", problem.GetProperty("title").GetString());
        Assert.Equal(
            "The down payment must be at least 10% of the property price.",
            problem.GetProperty("errors").GetProperty("downPayment")[0].GetString());
        Assert.Empty(host.Repository.Records);
    }

    [Fact]
    public async Task Post_WhenRequiredFieldsAreMissing_Returns400ListingEveryField()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.PostAsync("{}");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["annualEffectiveRate", "downPayment", "propertyPrice", "termInMonths"],
            ErrorFields(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Post_WhenOnlySomeFieldsAreMissing_ListsOnlyThose()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.PostAsync("""{"propertyPrice":280000,"downPayment":42000}""");

        Assert.Equal(["annualEffectiveRate", "termInMonths"], ErrorFields(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Post_WhenRateIsSentAsAPercentage_Returns400()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.PostAsync(
            """{"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":8.5,"termInMonths":240}""");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["annualEffectiveRate"], ErrorFields(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Post_WhenAFieldHasTheWrongJsonType_Returns400KeyedByTheCamelCaseField()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.PostAsync(
            """{"propertyPrice":"abc","downPayment":42000,"annualEffectiveRate":0.085,"termInMonths":240}""");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("propertyPrice", ErrorFields(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Post_WhenBodyIsMalformedJson_Returns400()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.PostAsync("{not json");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["request"], ErrorFields(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Post_WhenBodyIsEmpty_Returns400()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.PostAsync(string.Empty);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["request"], ErrorFields(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Post_WhenPropertyIsOutsideTheBonusRange_Returns200WithoutTheBonus()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.PostAsync(
            """{"propertyPrice":450000,"downPayment":90000,"annualEffectiveRate":0.085,"termInMonths":240,"applyGoodPayerBonus":true}""");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(360_000, body.GetProperty("financedAmount").GetDouble());
        Assert.False(body.GetProperty("benefit").GetProperty("eligible").GetBoolean());
        Assert.Equal(0, body.GetProperty("benefit").GetProperty("appliedAmount").GetDouble());
    }

    [Fact]
    public async Task Post_WhenAnonymous_SucceedsAndStoresNoBuyer()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.PostAsync(ContractExampleJson);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var record = Assert.Single(host.Repository.Records);
        Assert.Null(record.BuyerId);
    }

    [Fact]
    public async Task Post_WhenABuyerIsAuthenticated_StoresTheBuyerAndTheReturnedSimulationId()
    {
        await using var host = await SimulationsTestHost.StartAsync();
        var buyerId = Guid.NewGuid();

        var response = await host.PostAsync(ContractExampleJson, buyerId.ToString());

        var body = await ReadJsonAsync(response);
        var record = Assert.Single(host.Repository.Records);
        Assert.Equal(buyerId, record.BuyerId);
        Assert.Equal(body.GetProperty("simulationId").GetString(), record.Id.ToString());
    }

    [Fact]
    public async Task Post_WhenTheConfigurationDefinesInsurance_TheAssumptionsAreEchoedAndUsed()
    {
        await using var host = await SimulationsTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["Simulation:MonthlyCreditLifeInsurance"] = "35",
            ["Simulation:MonthlyPropertyInsurance"] = "20",
            ["Simulation:AdministrativeExpenses"] = "300",
        });

        var response = await host.PostAsync(ContractExampleJson);

        var body = await ReadJsonAsync(response);
        var assumptions = body.GetProperty("assumptions");
        Assert.Equal(35, assumptions.GetProperty("monthlyCreditLifeInsurance").GetDouble());
        Assert.Equal(20, assumptions.GetProperty("monthlyPropertyInsurance").GetDouble());
        Assert.Equal(300, assumptions.GetProperty("administrativeExpenses").GetDouble());
        Assert.Equal(0.10, assumptions.GetProperty("annualDiscountRate").GetDouble());
        Assert.Equal(240 * 55 + 300, body.GetProperty("additionalCosts").GetDouble(), 2);
        Assert.True(body.GetProperty("tcea").GetDouble() > 0.085);
    }
}
