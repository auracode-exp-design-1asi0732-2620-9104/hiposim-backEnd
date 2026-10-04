using HipoSim.FinancialEngine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HipoSim.Simulations;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the simulation endpoint, service and engines. The host must still register an
    /// <see cref="ISimulationRepository"/> and, optionally, a "Simulation" configuration section.
    /// </summary>
    public static IServiceCollection AddSimulations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers().AddApplicationPart(typeof(SimulationsController).Assembly);
        services.Configure<SimulationDefaults>(configuration.GetSection(SimulationDefaults.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        // Explicit instances: the DI container would inject an empty tier list into BenefitsEngine's optional parameter.
        services.TryAddSingleton(new BenefitsEngine());
        services.TryAddSingleton(new FinancialEngine.FinancialEngine());
        services.TryAddSingleton<SimulationRequestValidator>();
        services.TryAddScoped<ISimulationService, SimulationService>();
        return services;
    }
}
