using System.Security.Claims;
using HipoSim.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HipoSim.Api.Leads;

[ApiController]
[Authorize(Roles = "buyer")]
[Route("api/leads")]
public sealed class LeadsController(LeadService service) : ControllerBase
{
    [HttpPost]
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
}
