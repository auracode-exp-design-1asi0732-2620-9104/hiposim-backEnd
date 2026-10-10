using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HipoSim.Api.Auth;

/// <summary>Buyer signup (TS07 / US13) and buyer/advisor login (TS02 / TS07).</summary>
[AllowAnonymous]
[Route("api/auth")]
public sealed class AuthController(AuthService service) : ControllerBase
{
    /// <summary>Register a buyer account (TS07, US13)</summary>
    /// <remarks>
    /// Creates a user with the `buyer` role and signs the buyer in: the response carries the JWT and the user data.
    /// Both acceptances (terms and personal data processing, Law 29733) must be `true`; otherwise the account
    /// is not created. Public registration never grants the advisor role.
    /// </remarks>
    /// <param name="request">Buyer data and the two required acceptances.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <response code="201">Account created and session started. The body carries the JWT and the user.</response>
    /// <response code="400">Invalid data or an acceptance is `false`. `errors` lists each failing field by its camelCase name.</response>
    /// <response code="409">The email is already registered.</response>
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest? request, CancellationToken cancellationToken)
    {
        var errors = AccountValidation.Validate(request);
        MergeBindingErrors(errors);
        if (errors.Count != 0)
            return InvalidInput(errors);
        var result = await service.RegisterAsync(request!, cancellationToken);
        return result.Status == RegistrationStatus.Conflict
            ? Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already registered.")
            : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Log in and get a JWT (TS07, US22, US26)</summary>
    /// <remarks>
    /// Works for buyers and advisors. The role travels in the token and in `user.role`. The token of an advisor
    /// also carries the `agencyId` claim, so leads can be limited to the advisor's agency.
    /// A failed login never says which of the two values is wrong.
    /// </remarks>
    /// <param name="request">Email and password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <response code="200">Valid credentials. The body carries the JWT and the user.</response>
    /// <response code="400">Missing or malformed email or password. `errors` lists each failing field.</response>
    /// <response code="401">Invalid credentials. The message does not say which value is wrong.</response>
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request, CancellationToken cancellationToken)
    {
        var errors = AccountValidation.Validate(request);
        MergeBindingErrors(errors);
        if (errors.Count != 0)
            return InvalidInput(errors);
        var value = await service.LoginAsync(request!, cancellationToken);
        return value is null
            ? Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials.")
            : Ok(value);
    }

    private IActionResult InvalidInput(Dictionary<string, string[]> errors)
    {
        var details = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred."
        };
        details.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return ValidationProblem(details);
    }

    private void MergeBindingErrors(Dictionary<string, string[]> errors)
    {
        if (ModelState.IsValid) return;
        foreach (var (rawKey, value) in ModelState)
        {
            if (value.Errors.Count == 0) continue;
            var path = rawKey.StartsWith("$.", StringComparison.Ordinal) ? rawKey[2..] : rawKey;
            var key = path is "" or "$" ? "request" : JsonNamingPolicy.CamelCase.ConvertName(path);
            errors[key] = ["The value is invalid or malformed."];
        }
    }
}