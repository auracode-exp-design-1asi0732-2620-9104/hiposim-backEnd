namespace HipoSim.Api.Leads;

public sealed record CreateLeadRequest(
    Guid? AgencyId,
    Guid? SimulationId,
    bool? AcceptedContact);

public sealed record LeadResponse(
    Guid Id,
    Guid BuyerId,
    Guid AgencyId,
    Guid SimulationId,
    string Status,
    DateTimeOffset ConsentGrantedAt,
    DateTimeOffset CreatedAt);

public enum CreateLeadStatus
{
    Created,
    Invalid,
    AgencyNotFound,
    SimulationNotFound
}

public sealed record CreateLeadResult(
    CreateLeadStatus Status,
    LeadResponse? Value = null);