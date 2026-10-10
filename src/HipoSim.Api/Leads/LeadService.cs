
using HipoSim.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
    
    public async Task<IReadOnlyList<LeadInboxResponse>> GetInboxAsync(
        Guid agencyId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var query =
            from lead in database.Leads.AsNoTracking()
            join buyer in database.Users.AsNoTracking()
                on lead.BuyerId equals buyer.Id
            join simulation in database.Simulations.AsNoTracking()
                on lead.SimulationId equals simulation.Id
            where lead.AgencyId == agencyId
            select new
            {
                lead.Id,
                lead.BuyerId,
                BuyerName = buyer.FullName,
                BuyerEmail = buyer.Email,
                lead.SimulationId,
                simulation.ResponseJson,
                lead.Status,
                lead.CreatedAt
            };

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(l => l.Status == status);

        var records = await query
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

        return records.Select(record =>
        {
            double? monthlyInstallment = null;
            double? tcea = null;

            using var document = JsonDocument.Parse(record.ResponseJson);
            var root = document.RootElement;

            if (root.TryGetProperty("monthlyInstallment", out var installment) &&
                installment.ValueKind == JsonValueKind.Number)
            {
                monthlyInstallment = installment.GetDouble();
            }

            if (root.TryGetProperty("tcea", out var rate) &&
                rate.ValueKind == JsonValueKind.Number)
            {
                tcea = rate.GetDouble();
            }

            return new LeadInboxResponse(
                record.Id,
                record.BuyerId,
                record.BuyerName,
                record.BuyerEmail,
                record.SimulationId,
                monthlyInstallment,
                tcea,
                record.Status,
                record.CreatedAt);
        }).ToList();
    }

}
