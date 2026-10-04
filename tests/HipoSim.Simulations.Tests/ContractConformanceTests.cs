using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;

namespace HipoSim.Simulations.Tests;

/// <summary>
/// Keeps docs/api-contract/openapi.v0.yaml and the real endpoint in sync: the documented requests are sent to
/// the controller and the documented responses (numbers, messages, property names) must be what comes back.
/// </summary>
public class ContractConformanceTests
{
    private static readonly Lazy<Dictionary<object, object>> Contract = new(() =>
    {
        var yaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contract", "openapi.v0.yaml"));
        return new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(yaml);
    });

    private static readonly string[] SimulationPath = ["paths", "/api/simulations", "post"];

    private static object Node(params string[] path)
    {
        object current = Contract.Value;
        foreach (var key in path)
        {
            current = ((Dictionary<object, object>)current)[key];
        }

        return current;
    }

    private static Dictionary<object, object> Map(params string[] path) => (Dictionary<object, object>)Node(path);

    private static string[] SchemaProperties(string schemaName) =>
        Map("components", "schemas", schemaName, "properties").Keys.Cast<string>().Order().ToArray();

    private static string[] SchemaRequired(string schemaName) =>
        ((List<object>)Node("components", "schemas", schemaName, "required")).Cast<string>().Order().ToArray();

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order().ToArray();

    private static JsonNode? ToJson(object? node) => node switch
    {
        null => null,
        Dictionary<object, object> map => new JsonObject(
            map.Select(pair => KeyValuePair.Create(pair.Key.ToString()!, ToJson(pair.Value)))),
        List<object> list => new JsonArray(list.Select(ToJson).ToArray()),
        string text when bool.TryParse(text, out var flag) => JsonValue.Create(flag),
        string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole) => JsonValue.Create(whole),
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => JsonValue.Create(number),
        _ => JsonValue.Create(node.ToString()),
    };

    private static double Number(object? yamlScalar) => double.Parse((string)yamlScalar!, CultureInfo.InvariantCulture);

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> PostAsync(SimulationsTestHost host, string json)
    {
        var response = await host.PostAsync(json);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response, document.RootElement.Clone());
    }

    [Fact]
    public async Task Response_WhenCalculated_HasExactlyThePropertiesOfTheContract()
    {
        await using var host = await SimulationsTestHost.StartAsync();
        var requestExample = ToJson(Node([.. SimulationPath, "requestBody", "content", "application/json", "examples", "withBonus", "value"]));

        var (_, body) = await PostAsync(host, requestExample!.ToJsonString());

        Assert.Equal(SchemaProperties("SimulationResponse"), PropertyNames(body));
        Assert.Equal(SchemaRequired("SimulationResponse"), PropertyNames(body));
        Assert.Equal(SchemaProperties("BenefitResult"), PropertyNames(body.GetProperty("benefit")));
        Assert.Equal(SchemaProperties("SimulationAssumptions"), PropertyNames(body.GetProperty("assumptions")));
        Assert.Equal(SchemaProperties("AmortizationEntry"), PropertyNames(body.GetProperty("amortizationSchedule")[0]));
    }

    [Theory]
    [InlineData("withBonus")]
    [InlineData("outOfRange")]
    public async Task Post_WhenSendingAContractExample_ReturnsTheDocumentedResponse(string exampleName)
    {
        await using var host = await SimulationsTestHost.StartAsync();
        var request = ToJson(Node([.. SimulationPath, "requestBody", "content", "application/json", "examples", exampleName, "value"]));
        var expected = Map([.. SimulationPath, "responses", "200", "content", "application/json", "examples", exampleName, "value"]);

        var (response, body) = await PostAsync(host, request!.ToJsonString());

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        foreach (var field in new[] { "financedAmount", "monthlyInstallment", "totalPaid", "totalInterest", "additionalCosts", "tcea", "irr", "npv" })
        {
            Assert.Equal(Number(expected[field]), body.GetProperty(field).GetDouble(), 6);
        }

        var expectedBenefit = (Dictionary<object, object>)expected["benefit"];
        var benefit = body.GetProperty("benefit");
        Assert.Equal((string)expectedBenefit["name"], benefit.GetProperty("name").GetString());
        Assert.Equal(bool.Parse((string)expectedBenefit["eligible"]), benefit.GetProperty("eligible").GetBoolean());
        Assert.Equal(Number(expectedBenefit["appliedAmount"]), benefit.GetProperty("appliedAmount").GetDouble(), 6);

        var expectedAssumptions = (Dictionary<object, object>)expected["assumptions"];
        foreach (var field in SchemaProperties("SimulationAssumptions"))
        {
            Assert.Equal(Number(expectedAssumptions[field]), body.GetProperty("assumptions").GetProperty(field).GetDouble(), 6);
        }

        // The documented schedule shows the first two rows and the last one of the real 240.
        var schedule = body.GetProperty("amortizationSchedule");
        var documentedRows = (List<object>)expected["amortizationSchedule"];
        int[] actualIndexes = [0, 1, schedule.GetArrayLength() - 1];
        for (var row = 0; row < documentedRows.Count; row++)
        {
            var documented = (Dictionary<object, object>)documentedRows[row];
            var actual = schedule[actualIndexes[row]];
            Assert.Equal(int.Parse((string)documented["period"]), actual.GetProperty("period").GetInt32());
            Assert.Equal((string)documented["paymentDate"], actual.GetProperty("paymentDate").GetString());
            Assert.Equal(bool.Parse((string)documented["isGracePeriod"]), actual.GetProperty("isGracePeriod").GetBoolean());
            foreach (var field in new[] { "openingBalance", "installment", "interest", "amortization", "remainingBalance", "propertyInsurance", "creditLifeInsurance", "totalPayment" })
            {
                Assert.Equal(Number(documented[field]), actual.GetProperty(field).GetDouble(), 6);
            }
        }
    }

    [Theory]
    [InlineData("downPaymentTooLow", """{"propertyPrice":280000,"downPayment":10000,"annualEffectiveRate":0.085,"termInMonths":240}""")]
    [InlineData("missingFields", """{"downPayment":42000,"annualEffectiveRate":0.085}""")]
    [InlineData("rateAsPercentage", """{"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":8.5,"termInMonths":240}""")]
    public async Task Post_WhenTriggeringAContractError_ReturnsTheDocumentedProblem(string exampleName, string requestJson)
    {
        await using var host = await SimulationsTestHost.StartAsync();
        var expected = Map([.. SimulationPath, "responses", "400", "content", "application/problem+json", "examples", exampleName, "value"]);

        var (response, body) = await PostAsync(host, requestJson);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal((string)expected["title"], body.GetProperty("title").GetString());
        Assert.Equal(int.Parse((string)expected["status"]), body.GetProperty("status").GetInt32());

        var expectedErrors = (Dictionary<object, object>)expected["errors"];
        var actualErrors = body.GetProperty("errors");
        Assert.Equal(expectedErrors.Keys.Cast<string>().Order().ToArray(), PropertyNames(actualErrors));
        foreach (var (field, messages) in expectedErrors)
        {
            var documentedMessages = ((List<object>)messages).Cast<string>().ToArray();
            var actualMessages = actualErrors.GetProperty((string)field).EnumerateArray().Select(message => message.GetString()).ToArray();
            Assert.Equal(documentedMessages, actualMessages);
        }
    }
}
