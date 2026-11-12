using System.Security.Claims;
using System.Text.Json;
using HipoSim.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HipoSim.Api.Scenarios;

/// <summary>Saves one of the buyer's own simulations under a name (US06).</summary>
public sealed record CreateScenarioRequest(string? Name, Guid? SimulationId);

/// <summary>
/// A saved scenario with the figures of its simulation, so the apps can list and compare scenarios (US06, US07)
/// without calling the simulation endpoint again. A figure is null when the stored simulation does not carry it.
/// </summary>
public sealed record ScenarioResponse(
    Guid Id,
    string Name,
    Guid SimulationId,
    DateTimeOffset CreatedAt,
    double? PropertyPrice,
    double? DownPayment,
    double? AnnualEffectiveRate,
    int? TermInMonths,
    int? GracePeriodMonths,
    string? GraceType,
    bool? ApplyGoodPayerBonus,
    double? GoodPayerBonus,
    double? FinancedAmount,
    double? MonthlyInstallment,
    double? TotalPaid,
    double? TotalInterest,
    double? Tcea,
    double? Npv,
    double? Irr);

public static class ScenarioValidation
{
    public const int MaxNameLength = 60;

    public static Dictionary<string, string[]> Validate(CreateScenarioRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["request"] = ["The request body is required."];
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
            errors["name"] = [$"The name is required and cannot exceed {MaxNameLength} characters."];

        if (request.SimulationId is null || request.SimulationId == Guid.Empty)
            errors["simulationId"] = ["The simulationId is required."];

        return errors;
    }
}

[ApiController]
[Route("api/scenarios")]
[Authorize(Roles = "buyer")]
public sealed class ScenariosController(HipoSimDbContext database, TimeProvider clock) : ControllerBase
{
    /// <summary>Saves one of the buyer's simulations as a named scenario (US06).</summary>
    /// <response code="201">The scenario was saved.</response>
    /// <response code="400">Invalid data. `errors` lists each failing field by its camelCase name.</response>
    /// <response code="401">Missing or invalid token.</response>
    /// <response code="403">The token does not belong to a buyer.</response>
    /// <response code="404">The simulation does not exist or belongs to another buyer.</response>
    [HttpPost]
    [ProducesResponseType<ScenarioResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Create(
        [FromBody] CreateScenarioRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetBuyerId(out var buyerId))
            return Unauthorized();

        var errors = ScenarioValidation.Validate(request);
        MergeBindingErrors(errors);
        if (errors.Count != 0)
        {
            var details = new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            };
            details.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return ValidationProblem(details);
        }

        var simulation = await database.Simulations.AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.Id == request!.SimulationId!.Value && s.BuyerId == buyerId,
                cancellationToken);
        if (simulation is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Simulation not found."
            });
        }

        var scenario = new Scenario
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            SimulationId = simulation.Id,
            Name = request!.Name!.Trim(),
            CreatedAt = clock.GetUtcNow()
        };
        database.Scenarios.Add(scenario);
        await database.SaveChangesAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            ToResponse(scenario, simulation.InputJson, simulation.ResponseJson));
    }

    /// <summary>Lists the buyer's saved scenarios, newest first (US06).</summary>
    /// <response code="200">The buyer's scenarios. Empty when there are none.</response>
    /// <response code="401">Missing or invalid token.</response>
    /// <response code="403">The token does not belong to a buyer.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ScenarioResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetBuyerId(out var buyerId))
            return Unauthorized();

        var records = await (
            from scenario in database.Scenarios.AsNoTracking()
            join simulation in database.Simulations.AsNoTracking()
                on scenario.SimulationId equals simulation.Id
            where scenario.BuyerId == buyerId
            orderby scenario.CreatedAt descending
            select new { Scenario = scenario, simulation.InputJson, simulation.ResponseJson }
        ).ToListAsync(cancellationToken);

        return Ok(records.Select(r => ToResponse(r.Scenario, r.InputJson, r.ResponseJson)).ToList());
    }

    private bool TryGetBuyerId(out Guid buyerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out buyerId);

    private static ScenarioResponse ToResponse(Scenario scenario, string inputJson, string responseJson)
    {
        using var input = JsonDocument.Parse(inputJson);
        using var output = JsonDocument.Parse(responseJson);
        var request = input.RootElement;
        var result = output.RootElement;
        double? bonus = null;
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("benefit", out var benefit))
            bonus = Number(benefit, "appliedAmount");

        return new ScenarioResponse(
            scenario.Id,
            scenario.Name,
            scenario.SimulationId,
            scenario.CreatedAt,
            Number(request, "propertyPrice"),
            Number(request, "downPayment"),
            Number(request, "annualEffectiveRate"),
            Integer(request, "termInMonths"),
            Integer(request, "gracePeriodMonths"),
            Text(request, "graceType"),
            Boolean(request, "applyGoodPayerBonus"),
            bonus,
            Number(result, "financedAmount"),
            Number(result, "monthlyInstallment"),
            Number(result, "totalPaid"),
            Number(result, "totalInterest"),
            Number(result, "tcea"),
            Number(result, "npv"),
            Number(result, "irr"));
    }

    private static double? Number(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static int? Integer(JsonElement root, string name) =>
        Number(root, name) is { } value ? (int)Math.Round(value) : null;

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Boolean(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

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
