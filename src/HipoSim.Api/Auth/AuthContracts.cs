namespace HipoSim.Api.Auth;

public sealed record RegisterRequest(
    string? FullName,
    string? Email,
    string? Phone,
    string? Password,
    bool? AcceptedTerms,
    bool? AcceptedDataProcessing);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record UserResponse(Guid Id, string FullName, string Email, string Role);

public sealed record AuthResponse(string AccessToken, string TokenType, int ExpiresIn, UserResponse User);

public enum RegistrationStatus { Created, Invalid, Conflict }

public sealed record RegistrationResult(RegistrationStatus Status, AuthResponse? Value = null);
