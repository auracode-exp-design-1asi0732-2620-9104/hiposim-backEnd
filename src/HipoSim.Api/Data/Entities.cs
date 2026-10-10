namespace HipoSim.Api.Data;

public sealed class Agency
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AppUser
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string EmailNormalized { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "buyer";
    public Guid? AgencyId { get; set; }
    public DateTimeOffset AcceptedTermsAt { get; set; }
    public DateTimeOffset AcceptedDataProcessingAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ContactMessage
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class StoredSimulation
{
    public Guid Id { get; set; }
    public Guid? BuyerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string InputJson { get; set; } = "{}";
    public string ResponseJson { get; set; } = "{}";
}

public sealed class Lead
{
    public Guid Id { get; set; }
    public Guid BuyerId { get; set; }
    public Guid AgencyId { get; set; }
    public Guid SimulationId { get; set; }
    public string Status { get; set; } = "New";
    public DateTimeOffset ConsentGrantedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class LeadNote
{
    public Guid Id { get; set; }
    public Guid LeadId { get; set; }
    public Guid AuthorId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class LeadStatusChange
{
    public Guid Id { get; set; }
    public Guid LeadId { get; set; }
    public Guid ChangedBy { get; set; }
    public string PreviousStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
}
