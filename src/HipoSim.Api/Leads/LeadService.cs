using System.Text.Json;
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
            using var document = JsonDocument.Parse(record.ResponseJson);
            var root = document.RootElement;

            return new LeadInboxResponse(
                record.Id,
                record.BuyerId,
                record.BuyerName,
                record.BuyerEmail,
                record.SimulationId,
                GetNumber(root, "monthlyInstallment"),
                GetNumber(root, "tcea"),
                record.Status,
                record.CreatedAt);
        }).ToList();
    }

    public async Task<LeadDetailResponse?> GetDetailAsync(
        Guid leadId,
        Guid agencyId,
        CancellationToken cancellationToken = default)
    {
        var record = await (
            from lead in database.Leads.AsNoTracking()
            join buyer in database.Users.AsNoTracking()
                on lead.BuyerId equals buyer.Id
            join simulation in database.Simulations.AsNoTracking()
                on lead.SimulationId equals simulation.Id
            where lead.Id == leadId && lead.AgencyId == agencyId
            select new
            {
                Lead = lead,
                BuyerName = buyer.FullName,
                BuyerEmail = buyer.Email,
                BuyerPhone = buyer.Phone,
                simulation.InputJson,
                simulation.ResponseJson
            }
        ).FirstOrDefaultAsync(cancellationToken);

        if (record is null)
            return null;

        double? propertyPrice;
        double? downPayment;
        double? monthlyInstallment;
        double? tcea;

        using (var input = JsonDocument.Parse(record.InputJson))
        {
            propertyPrice = GetNumber(input.RootElement, "propertyPrice");
            downPayment = GetNumber(input.RootElement, "downPayment");
        }

        using (var output = JsonDocument.Parse(record.ResponseJson))
        {
            monthlyInstallment = GetNumber(
                output.RootElement, "monthlyInstallment");

            tcea = GetNumber(output.RootElement, "tcea");
        }

        var notes = await database.LeadNotes
            .AsNoTracking()
            .Where(n => n.LeadId == leadId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new LeadNoteResponse(
                n.Id,
                n.AuthorId,
                n.Content,
                n.CreatedAt))
            .ToListAsync(cancellationToken);

        var history = await database.LeadStatusChanges
            .AsNoTracking()
            .Where(h => h.LeadId == leadId)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new LeadStatusChangeResponse(
                h.Id,
                h.ChangedBy,
                h.PreviousStatus,
                h.NewStatus,
                h.ChangedAt))
            .ToListAsync(cancellationToken);

        var leadRecord = record.Lead;

        return new LeadDetailResponse(
            leadRecord.Id,
            leadRecord.BuyerId,
            record.BuyerName,
            record.BuyerEmail,
            record.BuyerPhone,
            leadRecord.AgencyId,
            leadRecord.SimulationId,
            propertyPrice,
            downPayment,
            monthlyInstallment,
            tcea,
            leadRecord.Status,
            leadRecord.ConsentGrantedAt,
            leadRecord.CreatedAt,
            notes,
            history);
    }
    
    public async Task<UpdateLeadStatusResult> UpdateStatusAsync(
        Guid leadId,
        Guid agencyId,
        Guid advisorId,
        UpdateLeadStatusRequest? request,
        CancellationToken cancellationToken = default)
    {
        string[] validStatuses =
        [
            "New",
            "InContact",
            "AppointmentScheduled",
            "Closed",
            "Discarded"
        ];

        if (request?.Status is null ||
            !validStatuses.Contains(request.Status))
        {
            return UpdateLeadStatusResult.Invalid;
        }

        var lead = await database.Leads
            .FirstOrDefaultAsync(
                l => l.Id == leadId && l.AgencyId == agencyId,
                cancellationToken);

        if (lead is null)
            return UpdateLeadStatusResult.NotFound;

        if (lead.Status == request.Status)
            return UpdateLeadStatusResult.Updated;

        var previousStatus = lead.Status;
        var now = clock.GetUtcNow();

        lead.Status = request.Status;

        database.LeadStatusChanges.Add(new LeadStatusChange
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            ChangedBy = advisorId,
            PreviousStatus = previousStatus,
            NewStatus = request.Status,
            ChangedAt = now
        });

        await database.SaveChangesAsync(cancellationToken);

        return UpdateLeadStatusResult.Updated;
    }
    
    public async Task<CreateLeadNoteResult> CreateNoteAsync(
        Guid leadId,
        Guid agencyId,
        Guid advisorId,
        CreateLeadNoteRequest? request,
        CancellationToken cancellationToken = default)
    {
        var content = request?.Content?.Trim();

        if (string.IsNullOrWhiteSpace(content) || content.Length > 2000)
            return new CreateLeadNoteResult(CreateLeadNoteStatus.Invalid);

        var leadExists = await database.Leads
            .AnyAsync(
                l => l.Id == leadId && l.AgencyId == agencyId,
                cancellationToken);

        if (!leadExists)
            return new CreateLeadNoteResult(CreateLeadNoteStatus.NotFound);

        var note = new LeadNote
        {
            Id = Guid.NewGuid(),
            LeadId = leadId,
            AuthorId = advisorId,
            Content = content,
            CreatedAt = clock.GetUtcNow()
        };

        database.LeadNotes.Add(note);
        await database.SaveChangesAsync(cancellationToken);

        return new CreateLeadNoteResult(
            CreateLeadNoteStatus.Created,
            new LeadNoteResponse(
                note.Id,
                note.AuthorId,
                note.Content,
                note.CreatedAt));
    }

    
    private static double? GetNumber(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }

        return null;
    }
}
