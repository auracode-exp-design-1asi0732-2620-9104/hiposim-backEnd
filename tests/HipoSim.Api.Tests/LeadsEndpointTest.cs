
using System.Net;
using System.Net.Http.Json;
using HipoSim.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HipoSim.Api.Tests;

public sealed class LeadsEndpointTests
{
    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"leads-test-{Guid.NewGuid()}";

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
    }

    [Fact]
    public async Task CreateLead_without_token_returns_unauthorized()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/leads", new
        {
            agencyId = Guid.NewGuid(),
            simulationId = Guid.NewGuid(),
            acceptedContact = true
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateLead_without_consent_returns_bad_request()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Test Buyer",
            email = "buyer@example.com",
            phone = "987654321",
            password = "Strong-Sample-2026",
            acceptedTerms = true,
            acceptedDataProcessing = true
        });

        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var registrationJson = System.Text.Json.JsonDocument.Parse(
            await registration.Content.ReadAsStringAsync());

        var token = registrationJson.RootElement
            .GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/leads", new
        {
            agencyId = Guid.NewGuid(),
            simulationId = Guid.NewGuid(),
            acceptedContact = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    
[Fact]
public async Task CreateLead_with_valid_consent_persists_new_lead()
{
    using var factory = new TestApiFactory();
    using var client = factory.CreateClient();

    var registration = await client.PostAsJsonAsync("/api/auth/register", new
    {
        fullName = "Test Buyer",
        email = "buyer@example.com",
        phone = "987654321",
        password = "Strong-Sample-2026",
        acceptedTerms = true,
        acceptedDataProcessing = true
    });

    Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

    using var registrationJson = System.Text.Json.JsonDocument.Parse(
        await registration.Content.ReadAsStringAsync());

    var buyerId = registrationJson.RootElement
        .GetProperty("user")
        .GetProperty("id")
        .GetGuid();

    var token = registrationJson.RootElement
        .GetProperty("accessToken")
        .GetString();

    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

    var agencyId = Guid.NewGuid();
    var simulationId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
        var database = scope.ServiceProvider
            .GetRequiredService<HipoSimDbContext>();

        database.Agencies.Add(new Agency
        {
            Id = agencyId,
            Name = "Test Agency",
            CreatedAt = DateTimeOffset.UtcNow
        });

        database.Simulations.Add(new StoredSimulation
        {
            Id = simulationId,
            BuyerId = buyerId,
            CreatedAt = DateTimeOffset.UtcNow,
            InputJson = "{}",
            ResponseJson = "{}"
        });

        await database.SaveChangesAsync();
    }

    var response = await client.PostAsJsonAsync("/api/leads", new
    {
        agencyId,
        simulationId,
        acceptedContact = true
    });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    using var verificationScope = factory.Services.CreateScope();
    var verificationDatabase = verificationScope.ServiceProvider
        .GetRequiredService<HipoSimDbContext>();

    var savedLead = await verificationDatabase.Leads.SingleAsync();

    Assert.Equal(buyerId, savedLead.BuyerId);
    Assert.Equal(agencyId, savedLead.AgencyId);
    Assert.Equal(simulationId, savedLead.SimulationId);
    Assert.Equal("New", savedLead.Status);
    Assert.NotEqual(default, savedLead.ConsentGrantedAt);
}

[Fact]
public async Task GetLeads_returns_only_advisor_agency_leads()
{
    using var factory = new TestApiFactory();
    using var client = factory.CreateClient();

    var agencyAId = Guid.NewGuid();
    var agencyBId = Guid.NewGuid();
    var advisorId = Guid.NewGuid();
    var buyerId = Guid.NewGuid();
    var simulationId = Guid.NewGuid();
    var leadAId = Guid.NewGuid();
    var leadBId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
        var database = scope.ServiceProvider
            .GetRequiredService<HipoSimDbContext>();

        database.Agencies.AddRange(
            new Agency
            {
                Id = agencyAId,
                Name = "Agency A",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Agency
            {
                Id = agencyBId,
                Name = "Agency B",
                CreatedAt = DateTimeOffset.UtcNow
            });

        database.Users.AddRange(
            new AppUser
            {
                Id = advisorId,
                FullName = "Test Advisor",
                Email = "advisor@example.com",
                EmailNormalized = "advisor@example.com",
                Role = "advisor",
                AgencyId = agencyAId
            },
            new AppUser
            {
                Id = buyerId,
                FullName = "Test Buyer",
                Email = "buyer@example.com",
                EmailNormalized = "buyer@example.com",
                Role = "buyer"
            });

        database.Simulations.Add(new StoredSimulation
        {
            Id = simulationId,
            BuyerId = buyerId,
            CreatedAt = DateTimeOffset.UtcNow,
            InputJson = "{}",
            ResponseJson = "{}"
        });
       
        database.Leads.AddRange(
            new Lead
            {
                Id = leadAId,
                BuyerId = buyerId,
                AgencyId = agencyAId,
                SimulationId = simulationId,
                Status = "New",
                ConsentGrantedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Lead
            {
                Id = leadBId,
                BuyerId = buyerId,
                AgencyId = agencyBId,
                SimulationId = simulationId,
                Status = "New",
                ConsentGrantedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });

        await database.SaveChangesAsync();
    }

    // Generate a JWT with the same issuer, audience and signing key
    // configured for the test server.
    var jwtSettings = new HipoSim.Api.Options.JwtOptions
    {
        Issuer = "HipoSimTests",
        Audience = "HipoSimTestClients",
        SigningKey = "Only_for_testserver_this_signing_key_is_long_enough_2026",
        ExpiresInSeconds = 3600
    };

    var tokenService = new HipoSim.Api.Auth.JwtTokenService(
        Microsoft.Extensions.Options.Options.Create(jwtSettings),
        TimeProvider.System);

    var advisor = new AppUser
    {
        Id = advisorId,
        Email = "advisor@example.com",
        FullName = "Test Advisor",
        Role = "advisor",
        AgencyId = agencyAId
    };

    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            tokenService.Create(advisor).AccessToken);

    var response = await client.GetAsync("/api/leads");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var json = System.Text.Json.JsonDocument.Parse(
        await response.Content.ReadAsStringAsync());

    var leads = json.RootElement.EnumerateArray().ToList();

    Assert.Single(leads);
    Assert.Equal(leadAId, leads[0].GetProperty("id").GetGuid());
    Assert.NotEqual(leadBId, leads[0].GetProperty("id").GetGuid());
}

[Fact]
public async Task GetLeadDetail_with_unknown_id_returns_not_found()
{
    using var factory = new TestApiFactory();
    using var client = factory.CreateClient();

    var agencyId = Guid.NewGuid();

    var advisor = new AppUser
    {
        Id = Guid.NewGuid(),
        FullName = "Test Advisor",
        Email = "advisor@example.com",
        EmailNormalized = "advisor@example.com",
        Role = "advisor",
        AgencyId = agencyId
    };

    using (var scope = factory.Services.CreateScope())
    {
        var database = scope.ServiceProvider
            .GetRequiredService<HipoSimDbContext>();

        database.Agencies.Add(new Agency
        {
            Id = agencyId,
            Name = "Test Agency",
            CreatedAt = DateTimeOffset.UtcNow
        });

        database.Users.Add(advisor);
        await database.SaveChangesAsync();
    }

    var jwtSettings = new HipoSim.Api.Options.JwtOptions
    {
        Issuer = "HipoSimTests",
        Audience = "HipoSimTestClients",
        SigningKey = "Only_for_testserver_this_signing_key_is_long_enough_2026",
        ExpiresInSeconds = 3600
    };

    var tokenService = new HipoSim.Api.Auth.JwtTokenService(
        Microsoft.Extensions.Options.Options.Create(jwtSettings),
        TimeProvider.System);

    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            tokenService.Create(advisor).AccessToken);

    var unknownLeadId = Guid.NewGuid();

    var response = await client.GetAsync($"/api/leads/{unknownLeadId}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task GetLeadDetail_from_another_agency_returns_not_found()
{
    using var factory = new TestApiFactory();
    using var client = factory.CreateClient();

    var agencyAId = Guid.NewGuid();
    var agencyBId = Guid.NewGuid();
    var buyerId = Guid.NewGuid();
    var simulationId = Guid.NewGuid();
    var leadId = Guid.NewGuid();

    var advisorB = new AppUser
    {
        Id = Guid.NewGuid(),
        FullName = "Advisor B",
        Email = "advisor.b@example.com",
        EmailNormalized = "advisor.b@example.com",
        Role = "advisor",
        AgencyId = agencyBId
    };

    using (var scope = factory.Services.CreateScope())
    {
        var database = scope.ServiceProvider
            .GetRequiredService<HipoSimDbContext>();

        database.Agencies.AddRange(
            new Agency
            {
                Id = agencyAId,
                Name = "Agency A",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Agency
            {
                Id = agencyBId,
                Name = "Agency B",
                CreatedAt = DateTimeOffset.UtcNow
            });

        database.Users.AddRange(
            advisorB,
            new AppUser
            {
                Id = buyerId,
                FullName = "Test Buyer",
                Email = "buyer@example.com",
                EmailNormalized = "buyer@example.com",
                Role = "buyer"
            });

        database.Simulations.Add(new StoredSimulation
        {
            Id = simulationId,
            BuyerId = buyerId,
            CreatedAt = DateTimeOffset.UtcNow,
            InputJson = "{}",
            ResponseJson = "{}"
        });

        database.Leads.Add(new Lead
        {
            Id = leadId,
            BuyerId = buyerId,
            AgencyId = agencyAId,
            SimulationId = simulationId,
            Status = "New",
            ConsentGrantedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await database.SaveChangesAsync();
    }

    var jwtSettings = new HipoSim.Api.Options.JwtOptions
    {
        Issuer = "HipoSimTests",
        Audience = "HipoSimTestClients",
        SigningKey = "Only_for_testserver_this_signing_key_is_long_enough_2026",
        ExpiresInSeconds = 3600
    };

    var tokenService = new HipoSim.Api.Auth.JwtTokenService(
        Microsoft.Extensions.Options.Options.Create(jwtSettings),
        TimeProvider.System);

    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            tokenService.Create(advisorB).AccessToken);

    var response = await client.GetAsync($"/api/leads/{leadId}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task UpdateLeadStatus_saves_status_and_history()
{
    using var factory = new TestApiFactory();
    using var client = factory.CreateClient();

    var agencyId = Guid.NewGuid();
    var buyerId = Guid.NewGuid();
    var simulationId = Guid.NewGuid();
    var leadId = Guid.NewGuid();

    var advisor = new AppUser
    {
        Id = Guid.NewGuid(),
        FullName = "Test Advisor",
        Email = "advisor@example.com",
        EmailNormalized = "advisor@example.com",
        Role = "advisor",
        AgencyId = agencyId
    };

    using (var scope = factory.Services.CreateScope())
    {
        var database = scope.ServiceProvider
            .GetRequiredService<HipoSimDbContext>();

        database.Agencies.Add(new Agency
        {
            Id = agencyId,
            Name = "Test Agency",
            CreatedAt = DateTimeOffset.UtcNow
        });

        database.Users.AddRange(
            advisor,
            new AppUser
            {
                Id = buyerId,
                FullName = "Test Buyer",
                Email = "buyer@example.com",
                EmailNormalized = "buyer@example.com",
                Role = "buyer"
            });

        database.Simulations.Add(new StoredSimulation
        {
            Id = simulationId,
            BuyerId = buyerId,
            CreatedAt = DateTimeOffset.UtcNow,
            InputJson = "{}",
            ResponseJson = "{}"
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

    var jwtSettings = new HipoSim.Api.Options.JwtOptions
    {
        Issuer = "HipoSimTests",
        Audience = "HipoSimTestClients",
        SigningKey = "Only_for_testserver_this_signing_key_is_long_enough_2026",
        ExpiresInSeconds = 3600
    };

    var tokenService = new HipoSim.Api.Auth.JwtTokenService(
        Microsoft.Extensions.Options.Options.Create(jwtSettings),
        TimeProvider.System);

    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            tokenService.Create(advisor).AccessToken);

    var response = await client.PatchAsJsonAsync(
        $"/api/leads/{leadId}/status",
        new { status = "InContact" });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var verificationScope = factory.Services.CreateScope();
    var verificationDatabase = verificationScope.ServiceProvider
        .GetRequiredService<HipoSimDbContext>();

    var savedLead = await verificationDatabase.Leads.SingleAsync();
    var history = await verificationDatabase.LeadStatusChanges.SingleAsync();

    Assert.Equal("InContact", savedLead.Status);
    Assert.Equal(leadId, history.LeadId);
    Assert.Equal(advisor.Id, history.ChangedBy);
    Assert.Equal("New", history.PreviousStatus);
    Assert.Equal("InContact", history.NewStatus);
}

[Fact]
public async Task UpdateLeadStatus_with_invalid_status_returns_bad_request()
{
    using var factory = new TestApiFactory();
    using var client = factory.CreateClient();

    var agencyId = Guid.NewGuid();

    var advisor = new AppUser
    {
        Id = Guid.NewGuid(),
        FullName = "Test Advisor",
        Email = "advisor@example.com",
        EmailNormalized = "advisor@example.com",
        Role = "advisor",
        AgencyId = agencyId
    };

    var jwtSettings = new HipoSim.Api.Options.JwtOptions
    {
        Issuer = "HipoSimTests",
        Audience = "HipoSimTestClients",
        SigningKey = "Only_for_testserver_this_signing_key_is_long_enough_2026",
        ExpiresInSeconds = 3600
    };

    var tokenService = new HipoSim.Api.Auth.JwtTokenService(
        Microsoft.Extensions.Options.Options.Create(jwtSettings),
        TimeProvider.System);

    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            tokenService.Create(advisor).AccessToken);

    var response = await client.PatchAsJsonAsync(
        $"/api/leads/{Guid.NewGuid()}/status",
        new { status = "InvalidStatus" });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task CreateLeadNote_with_empty_content_returns_bad_request()
{
    using var factory = new TestApiFactory();
    using var client = factory.CreateClient();

    var advisor = new AppUser
    {
        Id = Guid.NewGuid(),
        FullName = "Test Advisor",
        Email = "advisor@example.com",
        EmailNormalized = "advisor@example.com",
        Role = "advisor",
        AgencyId = Guid.NewGuid()
    };

    var jwtSettings = new HipoSim.Api.Options.JwtOptions
    {
        Issuer = "HipoSimTests",
        Audience = "HipoSimTestClients",
        SigningKey = "Only_for_testserver_this_signing_key_is_long_enough_2026",
        ExpiresInSeconds = 3600
    };

    var tokenService = new HipoSim.Api.Auth.JwtTokenService(
        Microsoft.Extensions.Options.Options.Create(jwtSettings),
        TimeProvider.System);

    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            tokenService.Create(advisor).AccessToken);

    var response = await client.PostAsJsonAsync(
        $"/api/leads/{Guid.NewGuid()}/notes",
        new { content = "   " });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task CreateLeadNote_with_valid_content_persists_note()
{
    using var factory = new TestApiFactory();
    using var client = factory.CreateClient();

    var agencyId = Guid.NewGuid();
    var buyerId = Guid.NewGuid();
    var simulationId = Guid.NewGuid();
    var leadId = Guid.NewGuid();

    var advisor = new AppUser
    {
        Id = Guid.NewGuid(),
        FullName = "Test Advisor",
        Email = "advisor@example.com",
        EmailNormalized = "advisor@example.com",
        Role = "advisor",
        AgencyId = agencyId
    };

    using (var scope = factory.Services.CreateScope())
    {
        var database = scope.ServiceProvider
            .GetRequiredService<HipoSimDbContext>();

        database.Agencies.Add(new Agency
        {
            Id = agencyId,
            Name = "Test Agency",
            CreatedAt = DateTimeOffset.UtcNow
        });

        database.Users.AddRange(
            advisor,
            new AppUser
            {
                Id = buyerId,
                FullName = "Test Buyer",
                Email = "buyer@example.com",
                EmailNormalized = "buyer@example.com",
                Role = "buyer"
            });

        database.Simulations.Add(new StoredSimulation
        {
            Id = simulationId,
            BuyerId = buyerId,
            CreatedAt = DateTimeOffset.UtcNow,
            InputJson = "{}",
            ResponseJson = "{}"
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

    var jwtSettings = new HipoSim.Api.Options.JwtOptions
    {
        Issuer = "HipoSimTests",
        Audience = "HipoSimTestClients",
        SigningKey = "Only_for_testserver_this_signing_key_is_long_enough_2026",
        ExpiresInSeconds = 3600
    };

    var tokenService = new HipoSim.Api.Auth.JwtTokenService(
        Microsoft.Extensions.Options.Options.Create(jwtSettings),
        TimeProvider.System);

    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            tokenService.Create(advisor).AccessToken);

    var response = await client.PostAsJsonAsync(
        $"/api/leads/{leadId}/notes",
        new { content = "Se contactó al comprador." });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    using var verificationScope = factory.Services.CreateScope();
    var verificationDatabase = verificationScope.ServiceProvider
        .GetRequiredService<HipoSimDbContext>();

    var savedNote = await verificationDatabase.LeadNotes.SingleAsync();

    Assert.Equal(leadId, savedNote.LeadId);
    Assert.Equal(advisor.Id, savedNote.AuthorId);
    Assert.Equal("Se contactó al comprador.", savedNote.Content);
    Assert.NotEqual(default, savedNote.CreatedAt);
}

}
