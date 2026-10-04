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
}
