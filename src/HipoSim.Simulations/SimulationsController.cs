using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HipoSim.Simulations;

// No [ApiController] on purpose: its automatic 400 would put binding errors under ASP.NET keys ("$.propertyPrice").
// The contract wants every error keyed by the camelCase request property, so the action builds the response itself.
[Route("api/simulations")]
public sealed class SimulationsController(ISimulationService service) : ControllerBase
{
    /// <summary>Calculates a mortgage simulation (TS06). Authentication is optional.</summary>
    [HttpPost]
    [ProducesResponseType<SimulationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Create([FromBody] SimulationRequest? request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidRequest(BindingErrors());
        }

        if (request is null)
        {
            return InvalidRequest(new Dictionary<string, string[]> { ["request"] = ["The request body is required."] });
        }

        try
        {
            return Ok(await service.CalculateAsync(request, GetBuyerId(), cancellationToken));
        }
        catch (SimulationValidationException exception)
        {
            return InvalidRequest(exception.Errors);
        }
    }

    private Guid? GetBuyerId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var buyerId) ? buyerId : null;
    }

    private IActionResult InvalidRequest(IReadOnlyDictionary<string, string[]> errors)
    {
        var details = new ValidationProblemDetails(new Dictionary<string, string[]>(errors))
        {
            Status = StatusCodes.Status400BadRequest,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        };
        details.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return ValidationProblem(details);
    }

    private Dictionary<string, string[]> BindingErrors()
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var (key, entry) in ModelState)
        {
            if (entry.Errors.Count == 0)
            {
                continue;
            }

            var field = ToCamelCasePath(key);
            errors[field] =
            [
                field == "request"
                    ? "The request body is not valid JSON."
                    : $"The {field} field has an invalid value.",
            ];
        }

        return errors;
    }

    private static string ToCamelCasePath(string key)
    {
        var path = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        return path is "" or "$"
            ? "request"
            : string.Join('.', path.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
    }
}
