
using HipoSim.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HipoSim.Api.Leads;

public sealed class LeadService(
    HipoSimDbContext database,
    TimeProvider clock)
{
    public async Task<CreateLeadResult> CreateAsync(
        Guid buyerId,
        CreateLeadRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            request.AgencyId is null ||
            request.AgencyId == Guid.Empty ||
            request.SimulationId is null ||
            request.SimulationId == Guid.Empty ||
            request.AcceptedContact != true)
        {
            return new CreateLeadResult(CreateLeadStatus.Invalid);
        }

        var agencyExists = await database.Agencies
            .AnyAsync(a => a.Id == request.AgencyId.Value, cancellationToken);

        if (!agencyExists)
            return new CreateLeadResult(CreateLeadStatus.AgencyNotFound);

        var simulationExists = await database.Simulations
            .AnyAsync(
                s => s.Id == request.SimulationId.Value &&
                     s.BuyerId == buyerId,
                cancellationToken);

        if (!simulationExists)
            return new CreateLeadResult(CreateLeadStatus.SimulationNotFound);

        var now = clock.GetUtcNow();

        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            AgencyId = request.AgencyId.Value,
            SimulationId = request.SimulationId.Value,
            Status = "New",
            ConsentGrantedAt = now,
            CreatedAt = now
        };

        database.Leads.Add(lead);
        await database.SaveChangesAsync(cancellationToken);

        return new CreateLeadResult(
            CreateLeadStatus.Created,
            new LeadResponse(
                lead.Id,
                lead.BuyerId,
                lead.AgencyId,
                lead.SimulationId,
                lead.Status,
                lead.ConsentGrantedAt,
                lead.CreatedAt));
    }
}
