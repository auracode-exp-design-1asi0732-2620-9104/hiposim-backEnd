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
