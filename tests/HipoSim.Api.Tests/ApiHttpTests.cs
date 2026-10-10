using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HipoSim.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HipoSim.Api.Tests;

public sealed class ApiHttpTests
{
    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = "hiposim-test-" + Guid.NewGuid();

                protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); // Prevent automatic PostgreSQL migration and seed.
            // Program.cs reads these values while the builder is created, before ConfigureAppConfiguration runs,
            // so they must be supplied as host settings.
            builder.UseSetting("ConnectionStrings:HipoSimDb", "Host=localhost;Database=unused_test_only;Username=unused;Password=unused");
            builder.UseSetting("Jwt:Issuer", "HipoSimTests");
            builder.UseSetting("Jwt:Audience", "HipoSimTestClients");
            builder.UseSetting("Jwt:SigningKey", "Only_for_testserver_this_signing_key_is_long_enough_2026");
            builder.UseSetting("Jwt:ExpiresInSeconds", "3600");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:HipoSimDb"] = "Host=localhost;Database=unused_test_only;Username=unused;Password=unused",
                    ["Jwt:Issuer"] = "HipoSimTests",
                    ["Jwt:Audience"] = "HipoSimTestClients",
                    ["Jwt:SigningKey"] = "Only_for_testserver_this_signing_key_is_long_enough_2026",
                    ["Jwt:ExpiresInSeconds"] = "3600"
                }));
            builder.ConfigureTestServices(services =>
            {
                // Replace PostgreSQL with EF Core InMemory in *tests only*; avoid requiring a DB server in CI.
                foreach (var item in services.Where(d => d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
                    && d.ServiceType.GenericTypeArguments.Contains(typeof(HipoSimDbContext))).ToArray())
                    services.Remove(item);
                foreach (var item in services.Where(d => d.ServiceType == typeof(DbContextOptions<HipoSimDbContext>)).ToArray())
                    services.Remove(item);
                services.AddDbContext<HipoSimDbContext>(options => options.UseInMemoryDatabase(databaseName));
            });
        }
    }

    [Fact]
    public async Task Health_returns_healthy_json()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Root_redirects_to_swagger_when_swagger_is_enabled()
    {
        using var factory = new TestApiFactory();
        using var client = factory
            .WithWebHostBuilder(builder => builder.UseSetting("Swagger:Enabled", "true"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/swagger", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Root_is_not_found_when_swagger_is_disabled()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Register_login_and_persist_simulation_end_to_end()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        var registration = new
        {
            fullName = "Ana Torres", email = "ana@example.com", phone = "987654321",
            password = "Strong-Sample-2026", acceptedTerms = true, acceptedDataProcessing = true
        };
        var register = await client.PostAsJsonAsync("/api/auth/register", registration);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var registrationJson = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        Assert.Equal("buyer", registrationJson.RootElement.GetProperty("user").GetProperty("role").GetString());
        var accessToken = registrationJson.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        var duplicate = await client.PostAsJsonAsync("/api/auth/register", registration);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var rejected = await client.PostAsJsonAsync("/api/auth/login", new { email = "ana@example.com", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "ana@example.com", password = "Strong-Sample-2026" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var simulation = await client.PostAsJsonAsync("/api/simulations", new
        {
            propertyPrice = 280000.0,
            downPayment = 42000.0,
            annualEffectiveRate = 0.085,
            termInMonths = 240,
            applyGoodPayerBonus = true,
            startDate = "2026-10-01"
        });
        Assert.Equal(HttpStatusCode.OK, simulation.StatusCode);
        using var simulationJson = JsonDocument.Parse(await simulation.Content.ReadAsStringAsync());
        var simulationId = simulationJson.RootElement.GetProperty("simulationId").GetGuid();
        Assert.NotEqual(Guid.Empty, simulationId);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HipoSimDbContext>();
        var record = await db.Simulations.SingleAsync();
        Assert.Equal(simulationId, record.Id);
        Assert.NotNull(record.BuyerId);
    }

    [Fact]
    public async Task Registration_rejects_missing_consent_with_problem_json()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Ana", email = "ana@example.com", phone = "987654321",
            password = "Strong-Sample-2026", acceptedTerms = false, acceptedDataProcessing = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("acceptedTerms", out _));
    }
}
