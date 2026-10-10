using System.Net;
using System.Text.Json;
using HipoSim.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HipoSim.Api.Tests;

public sealed class AgenciesEndpointTests
{
    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"agencies-test-{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:Issuer", "HipoSimTests");
            builder.UseSetting("Jwt:Audience", "HipoSimTestClients");
            builder.UseSetting(
                "Jwt:SigningKey",
                "Only_for_testserver_this_signing_key_is_long_enough_2026");
            builder.UseSetting("Jwt:ExpiresInSeconds", "3600");
            builder.UseSetting(
                "ConnectionStrings:HipoSimDb",
                "Host=localhost;Database=unused_test_only;Username=unused;Password=unused");

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:HipoSimDb"] =
                        "Host=localhost;Database=unused_test_only;Username=unused;Password=unused",
                    ["Jwt:Issuer"] = "HipoSimTests",
                    ["Jwt:Audience"] = "HipoSimTestClients",
                    ["Jwt:SigningKey"] =
                        "Only_for_testserver_this_signing_key_is_long_enough_2026",
                    ["Jwt:ExpiresInSeconds"] = "3600"
                }));

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

        public async Task SeedAsync(params Agency[] agencies)
        {
            using var scope = Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<HipoSimDbContext>();
            database.Agencies.AddRange(agencies);
            await database.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GetAgencies_without_agencies_returns_empty_list()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/agencies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetAgencies_without_token_returns_agencies_sorted_by_name()
    {
        using var factory = new TestApiFactory();
        var zeta = new Agency { Id = Guid.NewGuid(), Name = "Zeta Homes", CreatedAt = DateTimeOffset.UtcNow };
        await factory.SeedAsync(
            zeta,
            new Agency
            {
                Id = DatabaseSeed.DemoAgencyId,
                Name = "HipoSim Demo Agency",
                CreatedAt = DateTimeOffset.UtcNow
            });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/agencies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(DatabaseSeed.DemoAgencyId, items[0].GetProperty("id").GetGuid());
        Assert.Equal("HipoSim Demo Agency", items[0].GetProperty("name").GetString());
        Assert.Equal(zeta.Id, items[1].GetProperty("id").GetGuid());
        Assert.Equal("Zeta Homes", items[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetAgencies_exposes_only_id_and_name()
    {
        using var factory = new TestApiFactory();
        await factory.SeedAsync(new Agency
        {
            Id = DatabaseSeed.DemoAgencyId,
            Name = "HipoSim Demo Agency",
            CreatedAt = DateTimeOffset.UtcNow
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/agencies");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var properties = json.RootElement[0].EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["id", "name"], properties);
    }
}
