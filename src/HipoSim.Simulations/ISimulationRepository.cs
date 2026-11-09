using HipoSim.FinancialEngine;

namespace HipoSim.Simulations;

/// <summary>What is persisted for each calculation: the engine input (assumptions included) and the response.</summary>
public sealed record SimulationRecord(
    Guid Id,
    Guid? BuyerId,
    DateTimeOffset CreatedAt,
    LoanSimulationInput Input,
    SimulationResponse Response);

/// <summary>Persistence seam; the API host implements it with Entity Framework Core.</summary>
public interface ISimulationRepository
{
    Task AddAsync(SimulationRecord record, CancellationToken cancellationToken = default);

    /// <summary>The persisted simulation, or null when the id is unknown.</summary>
    Task<SimulationRecord?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}
