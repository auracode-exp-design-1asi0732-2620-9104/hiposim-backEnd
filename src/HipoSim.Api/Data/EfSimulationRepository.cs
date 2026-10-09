using System.Text.Json;
using HipoSim.Simulations;

namespace HipoSim.Api.Data;

/// <summary>Persists the existing TS06 simulation record without changing its financial calculations.</summary>
public sealed class EfSimulationRepository(HipoSimDbContext database) : ISimulationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AddAsync(SimulationRecord record, CancellationToken cancellationToken = default)
    {
        database.Simulations.Add(new StoredSimulation
        {
            Id = record.Id,
            BuyerId = record.BuyerId,
            CreatedAt = record.CreatedAt,
            InputJson = JsonSerializer.Serialize(record.Input, JsonOptions),
            ResponseJson = JsonSerializer.Serialize(record.Response, JsonOptions)
        });
        await database.SaveChangesAsync(cancellationToken);
    }
}
