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
    
public sealed record LeadInboxResponse(
    Guid Id,
    Guid BuyerId,
    string BuyerName,
    string BuyerEmail,
    Guid SimulationId,
    double? MonthlyInstallment,
    double? Tcea,
    string Status,
    DateTimeOffset CreatedAt);


public sealed record LeadDetailResponse(
    Guid Id,
    Guid BuyerId,
    string BuyerName,
    string BuyerEmail,
    string BuyerPhone,
    Guid AgencyId,
    Guid SimulationId,
    double? PropertyPrice,
    double? DownPayment,
    double? MonthlyInstallment,
    double? Tcea,
    string Status,
    DateTimeOffset ConsentGrantedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<LeadNoteResponse> Notes,
    IReadOnlyList<LeadStatusChangeResponse> StatusHistory);

public sealed record LeadNoteResponse(
    Guid Id,
    Guid AuthorId,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record LeadStatusChangeResponse(
    Guid Id,
    Guid ChangedBy,
    string PreviousStatus,
    string NewStatus,
    DateTimeOffset ChangedAt);

public sealed record UpdateLeadStatusRequest(
    string? Status);

public enum UpdateLeadStatusResult
{
    Updated,
    Invalid,
    NotFound
}
