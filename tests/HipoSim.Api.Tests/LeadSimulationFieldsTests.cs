using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HipoSim.Api.Auth;
using HipoSim.Api.Data;
using HipoSim.Api.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HipoSim.Api.Tests;

/// <summary>US23 and US24: the inbox and the lead detail carry the figures of the shared simulation.</summary>
public sealed class LeadSimulationFieldsTests
{
    private const string SigningKey = "Only_for_testserver_this_signing_key_is_long_enough_2026";

    private const string InputJson =
        """{"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":0.0885,"termInMonths":240,"applyGoodPayerBonus":true}""";

    private const string ResponseJson =
        """{"financedAmount":219800,"monthlyInstallment":1908.88,"tcea":0.0885,"benefit":{"requested":true,"eligible":true,"appliedAmount":18200}}""";

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"lead-fields-test-{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:Issuer", "HipoSimTests");
            builder.UseSetting("Jwt:Audience", "HipoSimTestClients");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
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

    /// <summary>Seeds an agency, its advisor, a buyer, a simulation and a lead; returns the lead id and an advisor client.</summary>
    private static async Task<(Guid LeadId, HttpClient Client)> SeedLeadAsync(
        TestApiFactory factory,
        string inputJson,
        string responseJson)
    {
        var agencyId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var simulationId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var advisor = new AppUser
        {
            Id = Guid.NewGuid(),
            FullName = "Advisor",
            Email = "advisor@example.com",
            EmailNormalized = "advisor@example.com",
            Role = "advisor",
            AgencyId = agencyId
        };

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HipoSimDbContext>();
            database.Agencies.Add(new Agency { Id = agencyId, Name = "Agency", CreatedAt = DateTimeOffset.UtcNow });
            database.Users.AddRange(
                advisor,
                new AppUser
                {
                    Id = buyerId,
                    FullName = "Test Buyer",
                    Email = "buyer@example.com",
                    EmailNormalized = "buyer@example.com",
                    Phone = "987654321",
                    Role = "buyer"
                });
            database.Simulations.Add(new StoredSimulation
            {
                Id = simulationId,
                BuyerId = buyerId,
                CreatedAt = DateTimeOffset.UtcNow,
                InputJson = inputJson,
                ResponseJson = responseJson
            });
            database.Leads.Add(new Lead
            {
                Id = leadId,
                BuyerId = buyerId,
                AgencyId = agencyId,
                SimulationId = simulationId,
                Status = "New",
                ConsentGrantedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await database.SaveChangesAsync();
        }

        var tokenService = new JwtTokenService(
            Microsoft.Extensions.Options.Options.Create(new JwtOptions
            {
                Issuer = "HipoSimTests",
                Audience = "HipoSimTestClients",
                SigningKey = SigningKey,
                ExpiresInSeconds = 3600
            }),
            TimeProvider.System);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenService.Create(advisor).AccessToken);
        return (leadId, client);
    }

    [Fact]
    public async Task Inbox_includes_the_property_price_of_the_simulation()
    {
        using var factory = new TestApiFactory();
        var (_, client) = await SeedLeadAsync(factory, InputJson, ResponseJson);

        var response = await client.GetAsync("/api/leads");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var lead = json.RootElement[0];
        Assert.Equal(280000, lead.GetProperty("propertyPrice").GetDouble());
        Assert.Equal(1908.88, lead.GetProperty("monthlyInstallment").GetDouble());
        Assert.Equal(0.0885, lead.GetProperty("tcea").GetDouble());
    }

    [Fact]
    public async Task Inbox_property_price_is_null_when_the_simulation_does_not_have_it()
    {
        using var factory = new TestApiFactory();
        var (_, client) = await SeedLeadAsync(factory, "{}", "{}");

        var response = await client.GetAsync("/api/leads");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement[0].GetProperty("propertyPrice").ValueKind);
    }

    [Fact]
    public async Task Detail_includes_bonus_financed_amount_and_term()
    {
        using var factory = new TestApiFactory();
        var (leadId, client) = await SeedLeadAsync(factory, InputJson, ResponseJson);

        var response = await client.GetAsync($"/api/leads/{leadId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var lead = json.RootElement;
        Assert.Equal(280000, lead.GetProperty("propertyPrice").GetDouble());
        Assert.Equal(42000, lead.GetProperty("downPayment").GetDouble());
        Assert.Equal(18200, lead.GetProperty("goodPayerBonus").GetDouble());
        Assert.Equal(219800, lead.GetProperty("financedAmount").GetDouble());
        Assert.Equal(240, lead.GetProperty("termInMonths").GetInt32());
        Assert.Equal(1908.88, lead.GetProperty("monthlyInstallment").GetDouble());
        Assert.Equal(0.0885, lead.GetProperty("tcea").GetDouble());
    }

    [Fact]
    public async Task Detail_new_fields_are_null_when_the_simulation_does_not_have_them()
    {
        using var factory = new TestApiFactory();
        var (leadId, client) = await SeedLeadAsync(factory, "{}", "{}");

        var response = await client.GetAsync($"/api/leads/{leadId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var name in new[] { "goodPayerBonus", "financedAmount", "termInMonths" })
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty(name).ValueKind);
    }
}
