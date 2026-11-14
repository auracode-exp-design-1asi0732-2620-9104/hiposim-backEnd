using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using HipoSim.Simulations.Reports;
using Microsoft.AspNetCore.Mvc;

namespace HipoSim.Simulations;

// No [ApiController] on purpose: its automatic 400 would put binding errors under ASP.NET keys ("$.propertyPrice").
// The contract wants every error keyed by the camelCase request property, so the action builds the response itself.
[Route("api/simulations")]
public sealed class SimulationsController(ISimulationService service, ISimulationReportService reports) : ControllerBase
{
    /// <summary>Calculate a mortgage simulation (TS06, US01, US02, US04, US05)</summary>
    /// <remarks>
    /// Converts the rate, applies the Good Payer Bonus when requested and the property is in range, builds the
    /// French amortization schedule and calculates TCEA, NPV and IRR.
    ///
    /// **Good Payer Bonus (US05).** With `applyGoodPayerBonus: true`, if the price is inside a bonus range the amount
    /// is subtracted from the financed amount (`benefit.eligible = true`). If the price is outside every range the
    /// response is still 200, `benefit.eligible = false`, `benefit.appliedAmount = 0` and the financed amount does
    /// not change. With `applyGoodPayerBonus: false` the bonus is never applied.
    ///
    /// `financedAmount = propertyPrice - downPayment - benefit.appliedAmount`.
    ///
    /// Authentication is optional: with a valid `Authorization` header the simulation is associated with the buyer.
    /// </remarks>
    /// <param name="request">Simulation data. Rates are fractions: `0.085` means 8.5%.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <response code="200">Simulation calculated: installment, TCEA, NPV, IRR, benefit and amortization schedule.</response>
    /// <response code="400">Invalid request (TS06 scenarios 2 and 3). `errors` lists every failing field by its camelCase name.</response>
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

    /// <summary>Download the PDF report of a simulation (TS03)</summary>
    /// <remarks>
    /// Returns the simulation summary and the full amortization schedule as a PDF document.
    ///
    /// A simulation calculated without signing in can be downloaded by anyone who has its `simulationId`.
    /// A simulation calculated by a signed-in buyer can be downloaded only by that buyer: any other caller gets 403.
    /// </remarks>
    /// <param name="id">The `simulationId` returned by `POST /api/simulations`.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <response code="200">The PDF document (`application/pdf`), sent as an attachment.</response>
    /// <response code="403">The simulation belongs to a buyer and the caller is not that buyer.</response>
    /// <response code="404">No simulation has that id.</response>
    [HttpGet("{id:guid}/report")]
    [Produces("application/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetReport(Guid id, CancellationToken cancellationToken)
    {
        var report = await reports.GetReportAsync(id, GetBuyerId(), cancellationToken);
        return report.Status switch
        {
            SimulationReportStatus.Ready => File(report.Pdf!, "application/pdf", report.FileName),
            SimulationReportStatus.Forbidden => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "The simulation belongs to another user."),
            _ => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Simulation not found."),
        };
    }

    private Guid? GetBuyerId()
    {
        // Preserve the original optional-auth behavior, but do not persist advisors as buyers.
        // Legacy test principals may contain NameIdentifier without a role.
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (role is not null && role != "buyer")
            return null;

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