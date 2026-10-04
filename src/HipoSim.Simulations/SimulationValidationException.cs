namespace HipoSim.Simulations;

/// <summary>A rule of the simulation request was broken; <see cref="Errors"/> is keyed by camelCase request property.</summary>
public sealed class SimulationValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The simulation request is not valid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
