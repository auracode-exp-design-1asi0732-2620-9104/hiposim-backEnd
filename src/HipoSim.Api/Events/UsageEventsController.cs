using System.Text.Json;
using System.Text.RegularExpressions;
using HipoSim.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HipoSim.Api.Events;

/// <summary>
/// An anonymous usage event sent by the Landing Page, the mobile app or the web app (TS09). It feeds the experiments
/// of chapter VIII, so it never carries personal data: the session is a random identifier created by the client.
/// </summary>
public sealed record CreateUsageEventRequest(
    string? SessionId,
    string? Source,
    string? Name,
    Dictionary<string, JsonElement>? Properties);

/// <summary>Confirmation that the event was stored.</summary>
public sealed record UsageEventResponse(Guid Id, DateTimeOffset ReceivedAt);

public static partial class UsageEventValidation
{
    public const int MaxProperties = 20;
    public const int MaxPropertyKeyLength = 40;
    public const int MaxPropertyTextLength = 100;
    public const int MaxPropertiesJsonLength = 2048;

    /// <summary>Products that can send events.</summary>
    public static readonly IReadOnlySet<string> Sources = new HashSet<string>(StringComparer.Ordinal)
    {
        "landing", "mobile", "web"
    };

    /// <summary>
    /// Closed list of event names. A new event is added here on purpose, so the KPI configuration and the clients
    /// always agree on the names.
    /// </summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "page_viewed",
        "app_opened",
        "simulation_started",
        "simulation_completed",
        "schedule_opened",
        "language_changed",
        "contact_message_sent",
        "scenario_saved",
        "scenarios_compared",
        "pdf_exported",
        "lead_sent"
    };

    /// <summary>Property keys that would carry personal data. Events are anonymous, so they are rejected.</summary>
    private static readonly string[] PersonalKeyFragments = ["email", "phone", "dni", "name", "password", "address"];

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex SessionIdPattern();

    [GeneratedRegex("^[a-z][a-zA-Z0-9_]{0,39}$")]
    private static partial Regex PropertyKeyPattern();

    public static Dictionary<string, string[]> Validate(CreateUsageEventRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["request"] = ["The request body is required."];
            return errors;
        }

        if (request.SessionId is null || !SessionIdPattern().IsMatch(request.SessionId))
            errors["sessionId"] = ["The sessionId must have 8 to 64 letters, digits, '-' or '_'."];

        if (request.Source is null || !Sources.Contains(request.Source))
            errors["source"] = [$"The source must be one of: {string.Join(", ", Sources.Order())}."];

        if (request.Name is null || !Names.Contains(request.Name))
            errors["name"] = [$"The event name must be one of: {string.Join(", ", Names.Order())}."];

        if (request.Properties is not null)
        {
            var problem = ValidateProperties(request.Properties);
            if (problem is not null)
                errors["properties"] = [problem];
        }

        return errors;
    }

    private static string? ValidateProperties(Dictionary<string, JsonElement> properties)
    {
        if (properties.Count > MaxProperties)
            return $"At most {MaxProperties} properties are allowed.";

        foreach (var (key, value) in properties)
        {
            if (!PropertyKeyPattern().IsMatch(key))
                return $"The property '{key}' must be camelCase and at most {MaxPropertyKeyLength} characters.";

            var lower = key.ToLowerInvariant();
            if (PersonalKeyFragments.Any(lower.Contains))
                return $"The property '{key}' looks like personal data; usage events are anonymous.";

            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    break;
                case JsonValueKind.String when value.GetString()!.Length <= MaxPropertyTextLength:
                    break;
                case JsonValueKind.String:
                    return $"The property '{key}' cannot exceed {MaxPropertyTextLength} characters.";
                default:
                    return $"The property '{key}' must be a text, a number or a boolean.";
            }
        }

        if (JsonSerializer.Serialize(properties).Length > MaxPropertiesJsonLength)
            return $"The properties cannot exceed {MaxPropertiesJsonLength} characters as JSON.";

        return null;
    }
}

[ApiController]
[Route("api/events")]
public sealed class UsageEventsController(HipoSimDbContext database, TimeProvider clock) : ControllerBase
{
    public const string RateLimitPolicy = "usage-events";

    /// <summary>Stores an anonymous usage event for the experiments (TS09). Anonymous and write-only.</summary>
    /// <response code="202">The event was stored.</response>
    /// <response code="400">Invalid data. `errors` lists each failing field by its camelCase name.</response>
    /// <response code="429">Too many events from the same address. Try again in a minute.</response>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    [RequestSizeLimit(8 * 1024)]
    [ProducesResponseType<UsageEventResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create(
        [FromBody] CreateUsageEventRequest? request,
        CancellationToken cancellationToken)
    {
        var errors = UsageEventValidation.Validate(request);
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

        var usageEvent = new UsageEvent
        {
            Id = Guid.NewGuid(),
            SessionId = request!.SessionId!,
            Source = request.Source!,
            Name = request.Name!,
            Properties = request.Properties is { Count: > 0 }
                ? JsonSerializer.Serialize(request.Properties)
                : null,
            OccurredAt = clock.GetUtcNow()
        };
        database.UsageEvents.Add(usageEvent);
        await database.SaveChangesAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status202Accepted,
            new UsageEventResponse(usageEvent.Id, usageEvent.OccurredAt));
    }

    private void MergeBindingErrors(Dictionary<string, string[]> errors)
    {
        if (ModelState.IsValid) return;
        foreach (var (rawKey, value) in ModelState)
        {
            if (value.Errors.Count == 0) continue;
            var path = rawKey.StartsWith("$.", StringComparison.Ordinal) ? rawKey[2..] : rawKey;
            var key = path is "" or "$" ? "request" : JsonNamingPolicy.CamelCase.ConvertName(path.Split('.')[0]);
            errors[key] = ["The value is invalid or malformed."];
        }
    }
}
