namespace HipoSim.Api.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int ExpiresInSeconds { get; set; } = 28800;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must be configured.");
        if (System.Text.Encoding.UTF8.GetByteCount(SigningKey) < 32)
            throw new InvalidOperationException("Jwt:SigningKey must have at least 32 UTF-8 bytes. Configure Jwt__SigningKey in production.");
        if (ExpiresInSeconds is < 300 or > 86400)
            throw new InvalidOperationException("Jwt:ExpiresInSeconds must be between 300 and 86400.");
    }
}
