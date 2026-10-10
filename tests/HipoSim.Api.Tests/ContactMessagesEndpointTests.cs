using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HipoSim.Api.Contact;
using HipoSim.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HipoSim.Api.Tests;

/// <summary>US19: the contact form of the Landing Page stores the message and confirms its reception.</summary>
public sealed class ContactMessagesEndpointTests
{
    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"contact-test-{Guid.NewGuid()}";

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
            builder.UseSetting("Cors:AllowedOrigins:0", "https://landing.example");

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

    private static object ValidMessage() => new
    {
        name = "Ana Torres",
        email = "ana@example.com",
        message = "Quisiera saber si el simulador considera el Bono del Buen Pagador."
    };

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("errors").Clone();
    }

    [Fact]
    public async Task Valid_message_is_stored_and_confirmed_without_a_token()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact-messages", new
        {
            name = "  Ana Torres ",
            email = " ana@example.com ",
            message = " Hola, tengo una consulta. "
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.True(json.RootElement.TryGetProperty("createdAt", out _));
        Assert.Equal(["id", "createdAt"], json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<HipoSimDbContext>().ContactMessages.SingleAsync();
        Assert.Equal(id, stored.Id);
        Assert.Equal("Ana Torres", stored.Name);
        Assert.Equal("ana@example.com", stored.Email);
        Assert.Equal("Hola, tengo una consulta.", stored.Message);
    }

    [Theory]
    [InlineData("ana")]
    [InlineData("ana@")]
    [InlineData("ana@example")]
    [InlineData("ana example@example.com")]
    [InlineData("")]
    public async Task Invalid_email_is_rejected_by_field(string email)
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact-messages", new
        {
            name = "Ana Torres",
            email,
            message = "Hola"
        });

        var errors = await ErrorsAsync(response);
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.False(errors.TryGetProperty("name", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_message_is_rejected_by_field(string message)
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact-messages", new
        {
            name = "Ana Torres",
            email = "ana@example.com",
            message
        });

        var errors = await ErrorsAsync(response);
        Assert.True(errors.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task Missing_fields_are_all_listed()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact-messages", new { });

        var errors = await ErrorsAsync(response);
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task Values_over_the_length_limits_are_rejected()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact-messages", new
        {
            name = new string('a', ContactMessageValidation.MaxNameLength + 1),
            email = new string('a', ContactMessageValidation.MaxEmailLength) + "@example.com",
            message = new string('a', ContactMessageValidation.MaxMessageLength + 1)
        });

        var errors = await ErrorsAsync(response);
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task Messages_cannot_be_read_back()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/contact-messages", ValidMessage());

        var response = await client.GetAsync("/api/contact-messages");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Sixth_message_in_a_minute_from_the_same_address_is_rejected()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        for (var i = 0; i < 5; i++)
        {
            var accepted = await client.PostAsJsonAsync("/api/contact-messages", ValidMessage());
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        var rejected = await client.PostAsJsonAsync("/api/contact-messages", ValidMessage());
        Assert.Equal((HttpStatusCode)429, rejected.StatusCode);

        using var other = factory.CreateClient();
        other.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.8");
        var fromAnotherAddress = await other.PostAsJsonAsync("/api/contact-messages", ValidMessage());
        Assert.Equal(HttpStatusCode.Created, fromAnotherAddress.StatusCode);
    }

    [Fact]
    public async Task Preflight_from_an_allowed_origin_is_accepted()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/contact-messages");
        request.Headers.Add("Origin", "https://landing.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Equal("https://landing.example", origins.Single());
    }
}
