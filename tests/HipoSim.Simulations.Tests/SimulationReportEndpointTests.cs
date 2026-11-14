using System.Net;
using System.Text;
using System.Text.Json;

namespace HipoSim.Simulations.Tests;

/// <summary>TS03 scenarios (200, 403, 404) against the real controller, service, engine and PDF generator.</summary>
public class SimulationReportEndpointTests
{
    private const string OwnerId = "11111111-1111-1111-1111-111111111111";
    private const string OtherBuyerId = "22222222-2222-2222-2222-222222222222";

    private const string ContractExampleJson = """
        {"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":0.085,"termInMonths":240,
         "applyGoodPayerBonus":true,"startDate":"2026-10-01"}
        """;

    private static async Task<string> CreateSimulationAsync(SimulationsTestHost host, string? buyerId = null)
    {
        var response = await host.PostAsync(ContractExampleJson, buyerId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("simulationId").GetString()!;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task GetReport_WhenTheSimulationWasCalculatedWithoutSigningIn_Returns200WithAPdf()
    {
        await using var host = await SimulationsTestHost.StartAsync();
        var id = await CreateSimulationAsync(host);

        var response = await host.GetAsync($"/api/simulations/{id}/report");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal($"hiposim-simulacion-{id}.pdf", response.Content.Headers.ContentDisposition?.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Equal("%%EOF", Encoding.ASCII.GetString(bytes, bytes.Length - 6, 5));
        Assert.Equal(bytes.Length, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task GetReport_WhenTheOwnerAsksForTheirSimulation_Returns200WithAPdf()
    {
        await using var host = await SimulationsTestHost.StartAsync();
        var id = await CreateSimulationAsync(host, OwnerId);

        var response = await host.GetAsync($"/api/simulations/{id}/report", OwnerId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetReport_WhenAnotherBuyerAsks_Returns403AsProblemJson()
    {
        await using var host = await SimulationsTestHost.StartAsync();
        var id = await CreateSimulationAsync(host, OwnerId);

        var response = await host.GetAsync($"/api/simulations/{id}/report", OtherBuyerId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(403, problem.GetProperty("status").GetInt32());
        Assert.Equal("The simulation belongs to another user.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetReport_WhenNobodyIsSignedInAndTheSimulationBelongsToABuyer_Returns403()
    {
        await using var host = await SimulationsTestHost.StartAsync();
        var id = await CreateSimulationAsync(host, OwnerId);

        var response = await host.GetAsync($"/api/simulations/{id}/report");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetReport_WhenTheIdDoesNotExist_Returns404AsProblemJson()
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.GetAsync($"/api/simulations/{Guid.NewGuid()}/report");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("Simulation not found.", problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    public async Task GetReport_WhenTheIdIsNotAGuid_Returns404(string id)
    {
        await using var host = await SimulationsTestHost.StartAsync();

        var response = await host.GetAsync($"/api/simulations/{id}/report");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReport_WhenTheSimulationHasGraceAndInsurance_Returns200()
    {
        await using var host = await SimulationsTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["Simulation:MonthlyPropertyInsurance"] = "20.5",
            ["Simulation:MonthlyCreditLifeInsurance"] = "10.25",
            ["Simulation:AdministrativeExpenses"] = "150",
        });
        var created = await host.PostAsync(
            """{"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":0.085,"termInMonths":240,"graceType":"total","gracePeriodMonths":6}""");
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("simulationId").GetString();

        var response = await host.GetAsync($"/api/simulations/{id}/report");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetReport_WhenAskedTwice_ReturnsTheSamePageCountBecauseTheContentIsDeterministic()
    {
        await using var host = await SimulationsTestHost.StartAsync();
        var id = await CreateSimulationAsync(host);

        var first = await (await host.GetAsync($"/api/simulations/{id}/report")).Content.ReadAsByteArrayAsync();
        var second = await (await host.GetAsync($"/api/simulations/{id}/report")).Content.ReadAsByteArrayAsync();

        Assert.Equal(PageCount(first), PageCount(second));
        Assert.True(PageCount(first) > 1, "A 240 month schedule does not fit in one page.");
    }

    // The page objects are listed uncompressed in the cross-reference structure: "/Type /Page" (and not "/Pages").
    private static int PageCount(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        return System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page(?![a-zA-Z])").Count;
    }
}
