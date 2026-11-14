using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

/// <summary>US06: a buyer saves a simulation under a name and lists the saved scenarios.</summary>
public sealed class ScenariosEndpointTests
{
    private const string SigningKey = "Only_for_testserver_this_signing_key_is_long_enough_2026";

    private const string InputJson =
        """{"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":0.0885,"termInMonths":240,"gracePeriodMonths":3,"graceType":"partial","applyGoodPayerBonus":true}""";

    private const string ResponseJson =
        """{"financedAmount":219800,"monthlyInstallment":1908.88,"totalPaid":458131.2,"totalInterest":238331.2,"tcea":0.0885,"npv":-21000.5,"irr":0.0885,"benefit":{"eligible":true,"appliedAmount":18200}}""";

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"scenarios-test-{Guid.NewGuid()}";

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

    private static AppUser NewUser(string email, string role) => new()
    {
        Id = Guid.NewGuid(),
        FullName = "Test " + role,
        Email = email,
        EmailNormalized = email,
        Phone = "987654321",
        Role = role
    };

    private static async Task<Guid> SeedSimulationAsync(TestApiFactory factory, AppUser? owner)
    {
        var simulationId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HipoSimDbContext>();
        if (owner is not null && !await database.Users.AnyAsync(u => u.Id == owner.Id))
            database.Users.Add(owner);
        database.Simulations.Add(new StoredSimulation
        {
            Id = simulationId,
            BuyerId = owner?.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            InputJson = InputJson,
            ResponseJson = ResponseJson
        });
        await database.SaveChangesAsync();
        return simulationId;
    }

    private static HttpClient ClientFor(TestApiFactory factory, AppUser user)
    {
        var tokens = new JwtTokenService(
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
            new AuthenticationHeaderValue("Bearer", tokens.Create(user).AccessToken);
        return client;
    }

    [Fact]
    public async Task Buyer_saves_own_simulation_and_gets_its_figures()
    {
        using var factory = new TestApiFactory();
        var buyer = NewUser("buyer@example.com", "buyer");
        var simulationId = await SeedSimulationAsync(factory, buyer);
        using var client = ClientFor(factory, buyer);

        var response = await client.PostAsJsonAsync("/api/scenarios", new { name = "  Plazo 20 años  ", simulationId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("Plazo 20 años", root.GetProperty("name").GetString());
        Assert.Equal(simulationId, root.GetProperty("simulationId").GetGuid());
        Assert.Equal(280000, root.GetProperty("propertyPrice").GetDouble());
        Assert.Equal(240, root.GetProperty("termInMonths").GetInt32());
        Assert.Equal(3, root.GetProperty("gracePeriodMonths").GetInt32());
        Assert.Equal("partial", root.GetProperty("graceType").GetString());
        Assert.True(root.GetProperty("applyGoodPayerBonus").GetBoolean());
        Assert.Equal(18200, root.GetProperty("goodPayerBonus").GetDouble());
        Assert.Equal(1908.88, root.GetProperty("monthlyInstallment").GetDouble());
        Assert.Equal(458131.2, root.GetProperty("totalPaid").GetDouble());
        Assert.Equal(0.0885, root.GetProperty("tcea").GetDouble());

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<HipoSimDbContext>().Scenarios.SingleAsync();
        Assert.Equal(buyer.Id, stored.BuyerId);
    }

    [Fact]
    public async Task Saving_a_simulation_of_another_buyer_returns_404()
    {
        using var factory = new TestApiFactory();
        var owner = NewUser("owner@example.com", "buyer");
        var other = NewUser("other@example.com", "buyer");
        var simulationId = await SeedSimulationAsync(factory, owner);
        await SeedSimulationAsync(factory, other);
        using var client = ClientFor(factory, other);

        var response = await client.PostAsJsonAsync("/api/scenarios", new { name = "Ajena", simulationId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Saving_an_anonymous_simulation_returns_404()
    {
        using var factory = new TestApiFactory();
        var buyer = NewUser("buyer@example.com", "buyer");
        await SeedSimulationAsync(factory, buyer);
        var anonymous = await SeedSimulationAsync(factory, owner: null);
        using var client = ClientFor(factory, buyer);

        var response = await client.PostAsJsonAsync("/api/scenarios", new { name = "Sin sesión", simulationId = anonymous });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Missing_name_and_simulation_are_listed_by_field()
    {
        using var factory = new TestApiFactory();
        var buyer = NewUser("buyer@example.com", "buyer");
        await SeedSimulationAsync(factory, buyer);
        using var client = ClientFor(factory, buyer);

        var response = await client.PostAsJsonAsync("/api/scenarios", new { name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = json.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("simulationId", out _));
    }

    [Fact]
    public async Task Name_over_the_limit_is_rejected()
    {
        using var factory = new TestApiFactory();
        var buyer = NewUser("buyer@example.com", "buyer");
        var simulationId = await SeedSimulationAsync(factory, buyer);
        using var client = ClientFor(factory, buyer);

        var response = await client.PostAsJsonAsync(
            "/api/scenarios",
            new { name = new string('a', Scenarios.ScenarioValidation.MaxNameLength + 1), simulationId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_returns_only_own_scenarios_newest_first()
    {
        using var factory = new TestApiFactory();
        var buyer = NewUser("buyer@example.com", "buyer");
        var other = NewUser("other@example.com", "buyer");
        var first = await SeedSimulationAsync(factory, buyer);
        var second = await SeedSimulationAsync(factory, buyer);
        var foreign = await SeedSimulationAsync(factory, other);
        using var buyerClient = ClientFor(factory, buyer);
        using var otherClient = ClientFor(factory, other);
        await buyerClient.PostAsJsonAsync("/api/scenarios", new { name = "Primero", simulationId = first });
        await Task.Delay(20);
        await buyerClient.PostAsJsonAsync("/api/scenarios", new { name = "Segundo", simulationId = second });
        await otherClient.PostAsJsonAsync("/api/scenarios", new { name = "De otro", simulationId = foreign });

        var response = await buyerClient.GetAsync("/api/scenarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = json.RootElement.EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(["Segundo", "Primero"], names);
    }

    [Fact]
    public async Task Scenarios_require_a_buyer_token()
    {
        using var factory = new TestApiFactory();
        var advisor = NewUser("advisor@example.com", "advisor");
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HipoSimDbContext>();
            database.Users.Add(advisor);
            await database.SaveChangesAsync();
        }

        using var anonymous = factory.CreateClient();
        using var advisorClient = ClientFor(factory, advisor);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/scenarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await advisorClient.GetAsync("/api/scenarios")).StatusCode);
    }
}
