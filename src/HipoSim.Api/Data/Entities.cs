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

public sealed class StoredSimulation
{
    public Guid Id { get; set; }
    public Guid? BuyerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string InputJson { get; set; } = "{}";
    public string ResponseJson { get; set; } = "{}";
}
