namespace HipoSim.Api.Auth;

/// <summary>Body of POST /api/auth/register.</summary>
/// <param name="FullName">Full name of the buyer. Required, up to 150 characters.</param>
/// <param name="Email">Email address. Required and unique.</param>
/// <param name="Phone">Mobile phone number. Required, up to 20 characters.</param>
/// <param name="Password">Password. Required, from 8 to 128 characters.</param>
/// <param name="AcceptedTerms">Must be true: Terms and Conditions.</param>
/// <param name="AcceptedDataProcessing">Must be true: authorization for the processing of personal data (Law 29733).</param>
public sealed record RegisterRequest(
    string? FullName,
    string? Email,
    string? Phone,
    string? Password,
    bool? AcceptedTerms,
    bool? AcceptedDataProcessing);

/// <summary>Body of POST /api/auth/login.</summary>
/// <param name="Email">Email address of the account.</param>
/// <param name="Password">Password of the account.</param>
public sealed record LoginRequest(string? Email, string? Password);

/// <summary>Authenticated user data.</summary>
/// <param name="Id">User identifier.</param>
/// <param name="FullName">Full name.</param>
/// <param name="Email">Email address.</param>
/// <param name="Role">Either `buyer` or `advisor`.</param>
public sealed record UserResponse(Guid Id, string FullName, string Email, string Role);

/// <summary>Session returned by register and login.</summary>
/// <param name="AccessToken">JWT to send as `Authorization: Bearer`.</param>
/// <param name="TokenType">Always `Bearer`.</param>
/// <param name="ExpiresIn">Token lifetime in seconds.</param>
/// <param name="User">The authenticated user.</param>
public sealed record AuthResponse(string AccessToken, string TokenType, int ExpiresIn, UserResponse User);

public enum RegistrationStatus { Created, Invalid, Conflict }

public sealed record RegistrationResult(RegistrationStatus Status, AuthResponse? Value = null);