using System.Net.Mail;
using System.Text.Json;
using HipoSim.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HipoSim.Api.Contact;

/// <summary>A question sent from the contact form of the Landing Page (US19).</summary>
public sealed record CreateContactMessageRequest(string? Name, string? Email, string? Message);

/// <summary>Confirmation that the message was stored.</summary>
public sealed record ContactMessageResponse(Guid Id, DateTimeOffset CreatedAt);

public static class ContactMessageValidation
{
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 150;
    public const int MaxMessageLength = 2000;

    public static Dictionary<string, string[]> Validate(CreateContactMessageRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["request"] = ["The request body is required."];
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
            errors["name"] = [$"Name is required and cannot exceed {MaxNameLength} characters."];

        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > MaxEmailLength ||
            !MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase) ||
            !parsed.Host.Contains('.'))
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Trim().Length > MaxMessageLength)
            errors["message"] = [$"Message is required and cannot exceed {MaxMessageLength} characters."];

        return errors;
    }
}

[ApiController]
[Route("api/contact-messages")]
public sealed class ContactMessagesController(HipoSimDbContext database, TimeProvider clock) : ControllerBase
{
    public const string RateLimitPolicy = "contact-messages";

    /// <summary>Stores a contact message and confirms its reception. Anonymous and write-only.</summary>
    /// <response code="201">The message was stored.</response>
    /// <response code="400">Invalid data. `errors` lists each failing field by its camelCase name.</response>
    /// <response code="429">Too many messages from the same address. Try again in a minute.</response>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    [RequestSizeLimit(16 * 1024)]
    [ProducesResponseType<ContactMessageResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create(
        [FromBody] CreateContactMessageRequest? request,
        CancellationToken cancellationToken)
    {
        var errors = ContactMessageValidation.Validate(request);
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

        var message = new ContactMessage
        {
            Id = Guid.NewGuid(),
            Name = request!.Name!.Trim(),
            Email = request.Email!.Trim(),
            Message = request.Message!.Trim(),
            CreatedAt = clock.GetUtcNow()
        };
        database.ContactMessages.Add(message);
        await database.SaveChangesAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new ContactMessageResponse(message.Id, message.CreatedAt));
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
