namespace HipoSim.Simulations.Reports;

public enum SimulationReportStatus
{
    /// <summary>The report was generated.</summary>
    Ready,

    /// <summary>No simulation has that id.</summary>
    NotFound,

    /// <summary>The simulation belongs to another buyer, or to a buyer and the caller is not signed in as that buyer.</summary>
    Forbidden,
}

/// <summary>Outcome of a report request; <see cref="Pdf"/> and <see cref="FileName"/> are set only when <see cref="Status"/> is Ready.</summary>
public sealed record SimulationReportResult(SimulationReportStatus Status, byte[]? Pdf = null, string? FileName = null);

public interface ISimulationReportService
{
    /// <param name="simulationId">Id returned by <c>POST /api/simulations</c>.</param>
    /// <param name="buyerId">The signed-in buyer, or null for an anonymous caller or an advisor.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<SimulationReportResult> GetReportAsync(Guid simulationId, Guid? buyerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Finds the simulation, applies the access rule and renders the PDF.
/// A simulation saved without a buyer (anonymous calculation) can be read by anyone who has its id, which is a random GUID
/// returned only to whoever created it. A simulation saved by a buyer can be read only by that buyer.
/// </summary>
public sealed class SimulationReportService(
    ISimulationRepository repository,
    ISimulationReportGenerator generator) : ISimulationReportService
{
    public async Task<SimulationReportResult> GetReportAsync(
        Guid simulationId,
        Guid? buyerId,
        CancellationToken cancellationToken = default)
    {
        var record = await repository.FindAsync(simulationId, cancellationToken);
        if (record is null)
        {
            return new SimulationReportResult(SimulationReportStatus.NotFound);
        }

        if (record.BuyerId is { } owner && owner != buyerId)
        {
            return new SimulationReportResult(SimulationReportStatus.Forbidden);
        }

        var content = SimulationReportContentBuilder.Build(record);
        return new SimulationReportResult(SimulationReportStatus.Ready, generator.Generate(content), content.FileName);
    }
}
