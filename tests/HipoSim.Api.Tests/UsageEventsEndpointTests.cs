using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HipoSim.Api.Data;
using HipoSim.Api.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HipoSim.Api.Tests;

/// <summary>TS09: the products store anonymous usage events for the experiments of chapter VIII.</summary>
public sealed class UsageEventsEndpointTests
{
    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"events-test-{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:Issuer", "HipoSimTests");
            builder.UseSetting("Jwt:Audience", "HipoSimTestClients");
            builder.UseSetting("Jwt:SigningKey", "Only_for_testserver_this_signing_key_is_long_enough_2026");
            builder.UseSetting("Jwt:ExpiresInSeconds", "3600");
            builder.UseSetting(
                "ConnectionStrings:HipoSimDb",
                "Host=localhost;Database=unused_test_only;Username=unused;Password=unused");

            builder.ConfigureTestServices(services =>
            {
                foreach (var item in services.Where(d =>
                    d.ServiceType.IsGenericType &&
                    d.ServiceType.GetGenericTypeDefinition().Name.StartsWith(
                        "IDbContextOptionsConfiguration",
                        StringComparison.Ordinal) &&
                    d.ServiceType.GenericTypeArguments.Contains(
                        typeof(HipoSimDbContext))).ToArray())
                {
                    services.Remove(item);
                }

                foreach (var item in services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<HipoSimDbContext>))
                    .ToArray())
                {
                    services.Remove(item);
                }

                services.AddDbContext<HipoSimDbContext>(
                    options => options.UseInMemoryDatabase(databaseName));
            });
        }
    }

    private static object ValidEvent() => new
    {
        sessionId = "3f9b8c2e-6a1d-4c58",
        source = "mobile",
        name = "simulation_completed",
        properties = new { termInMonths = 240, applyGoodPayerBonus = true, graceType = "none" }
    };

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("errors").Clone();
    }

    [Fact]
    public async Task Valid_event_is_stored_without_a_token()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/events", ValidEvent());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();
        Assert.True(json.RootElement.TryGetProperty("receivedAt", out _));

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<HipoSimDbContext>().UsageEvents.SingleAsync();
        Assert.Equal(id, stored.Id);
        Assert.Equal("3f9b8c2e-6a1d-4c58", stored.SessionId);
        Assert.Equal("mobile", stored.Source);
        Assert.Equal("simulation_completed", stored.Name);
        using var properties = JsonDocument.Parse(stored.Properties!);
        Assert.Equal(240, properties.RootElement.GetProperty("termInMonths").GetInt32());
        Assert.True(properties.RootElement.GetProperty("applyGoodPayerBonus").GetBoolean());
    }

    [Fact]
    public async Task Event_without_properties_is_stored_with_null_properties()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/events", new
        {
            sessionId = "landing-session-01",
            source = "landing",
            name = "page_viewed"
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<HipoSimDbContext>().UsageEvents.SingleAsync();
        Assert.Null(stored.Properties);
    }

    [Theory]
    [InlineData("unknown_event")]
    [InlineData("Simulation_Completed")]
    [InlineData("")]
    public async Task Event_names_outside_the_closed_list_are_rejected(string name)
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/events", new
        {
            sessionId = "3f9b8c2e-6a1d-4c58",
            source = "mobile",
            name
        });

        var errors = await ErrorsAsync(response);
        Assert.True(errors.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Unknown_source_and_short_session_are_rejected_by_field()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/events", new
        {
            sessionId = "abc",
            source = "desktop",
            name = "app_opened"
        });

        var errors = await ErrorsAsync(response);
        Assert.True(errors.TryGetProperty("sessionId", out _));
        Assert.True(errors.TryGetProperty("source", out _));
        Assert.False(errors.TryGetProperty("name", out _));
    }

    [Theory]
    [InlineData("email")]
    [InlineData("buyerPhone")]
    [InlineData("fullName")]
    public async Task Properties_that_look_like_personal_data_are_rejected(string key)
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/events", new Dictionary<string, object>
        {
            ["sessionId"] = "3f9b8c2e-6a1d-4c58",
            ["source"] = "mobile",
            ["name"] = "lead_sent",
            ["properties"] = new Dictionary<string, object> { [key] = "ana@example.com" }
        });

        var errors = await ErrorsAsync(response);
        Assert.True(errors.TryGetProperty("properties", out _));
    }

    [Fact]
    public async Task Nested_property_values_are_rejected()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/events", new
        {
            sessionId = "3f9b8c2e-6a1d-4c58",
            source = "mobile",
            name = "simulation_completed",
            properties = new { result = new { tcea = 0.0885 } }
        });

        var errors = await ErrorsAsync(response);
        Assert.True(errors.TryGetProperty("properties", out _));
    }

    [Fact]
    public async Task Too_many_properties_are_rejected()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        var properties = Enumerable.Range(0, UsageEventValidation.MaxProperties + 1)
            .ToDictionary(i => $"key{i}", i => (object)i);

        var response = await client.PostAsJsonAsync("/api/events", new Dictionary<string, object>
        {
            ["sessionId"] = "3f9b8c2e-6a1d-4c58",
            ["source"] = "mobile",
            ["name"] = "app_opened",
            ["properties"] = properties
        });

        var errors = await ErrorsAsync(response);
        Assert.True(errors.TryGetProperty("properties", out _));
    }

    [Fact]
    public async Task Events_cannot_be_read_back()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/events", ValidEvent());

        var response = await client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Event_over_the_rate_limit_from_the_same_address_is_rejected()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");

        for (var i = 0; i < 120; i++)
        {
            var accepted = await client.PostAsJsonAsync("/api/events", ValidEvent());
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        var rejected = await client.PostAsJsonAsync("/api/events", ValidEvent());
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }
}
