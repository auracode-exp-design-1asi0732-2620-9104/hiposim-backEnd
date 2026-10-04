using System.Security.Claims;
using System.Text;
using HipoSim.Simulations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HipoSim.Simulations.Tests;

internal sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal sealed class RecordingRepository : ISimulationRepository
{
    public List<SimulationRecord> Records { get; } = [];

    public Task AddAsync(SimulationRecord record, CancellationToken cancellationToken = default)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }
}

/// <summary>The real controller, service and engines hosted in memory with a recording repository.</summary>
internal sealed class SimulationsTestHost : IAsyncDisposable
{
    public const string BuyerHeader = "X-Test-Buyer-Id";

    private readonly WebApplication _app;

    private SimulationsTestHost(WebApplication app, RecordingRepository repository)
    {
        _app = app;
        Repository = repository;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public RecordingRepository Repository { get; }

    public static async Task<SimulationsTestHost> StartAsync(IDictionary<string, string?>? configuration = null)
    {
        var repository = new RecordingRepository();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(configuration ?? new Dictionary<string, string?>());
        builder.Services.AddSingleton<ISimulationRepository>(repository);
        builder.Services.AddSimulations(builder.Configuration);

        var app = builder.Build();
        // Stands in for the JWT middleware of the API host: a header carries the authenticated buyer id.
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue(BuyerHeader, out var buyerId))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, buyerId.ToString())], authenticationType: "test"));
            }

            await next();
        });
        app.MapControllers();
        await app.StartAsync();

        return new SimulationsTestHost(app, repository);
    }

    public async Task<HttpResponseMessage> PostAsync(string json, string? buyerId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/simulations")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (buyerId is not null)
        {
            request.Headers.Add(BuyerHeader, buyerId);
        }

        return await Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
