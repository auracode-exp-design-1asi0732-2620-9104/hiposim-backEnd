using System.Security.Claims;
using HipoSim.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HipoSim.Api.Leads;

[ApiController]
[Authorize]
[Route("api/leads")]
public sealed class LeadsController(LeadService service) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "buyer")]
    [ProducesResponseType<LeadResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] CreateLeadRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out var buyerId))
            return Unauthorized();

        var result = await service.CreateAsync(
            buyerId,
            request,
            cancellationToken);

        return result.Status switch
        {
            CreateLeadStatus.Created =>
                StatusCode(StatusCodes.Status201Created, result.Value),

            CreateLeadStatus.Invalid =>
                BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Valid agency, simulation and consent are required."
                }),

            CreateLeadStatus.AgencyNotFound =>
                NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Agency not found."
                }),

            CreateLeadStatus.SimulationNotFound =>
                NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Simulation not found."
                }),

            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
    
    [HttpGet]
    [Authorize(Roles = "advisor")]
    [ProducesResponseType<IReadOnlyList<LeadInboxResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInbox(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var agencyClaim = User.FindFirstValue("agencyId");

        if (!Guid.TryParse(agencyClaim, out var agencyId) ||
            agencyId == Guid.Empty)
        {
            return Forbid();
        }

        var leads = await service.GetInboxAsync(
            agencyId,
            status,
            cancellationToken);

        return Ok(leads);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "advisor")]
    [ProducesResponseType<LeadDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(
        Guid id,
        CancellationToken cancellationToken)
    {
        var agencyClaim = User.FindFirstValue("agencyId");

        if (!Guid.TryParse(agencyClaim, out var agencyId) ||
            agencyId == Guid.Empty)
        {
            return Forbid();
        }

        var lead = await service.GetDetailAsync(
            id,
            agencyId,
            cancellationToken);

        return lead is null ? NotFound() : Ok(lead);
    }
    
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "advisor")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateLeadStatusRequest? request,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var agencyClaim = User.FindFirstValue("agencyId");

        if (!Guid.TryParse(userIdClaim, out var advisorId) ||
            !Guid.TryParse(agencyClaim, out var agencyId) ||
            advisorId == Guid.Empty ||
            agencyId == Guid.Empty)
        {
            return Forbid();
        }

        var result = await service.UpdateStatusAsync(
            id,
            agencyId,
            advisorId,
            request,
            cancellationToken);

        return result switch
        {
            UpdateLeadStatusResult.Updated => Ok(
                new { id, status = request!.Status }),

            UpdateLeadStatusResult.Invalid => BadRequest(
                new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid lead status."
                }),

            UpdateLeadStatusResult.NotFound => NotFound(),

            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

}
