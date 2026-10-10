using HipoSim.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HipoSim.Api.Agencies;

/// <summary>An agency a buyer can send a simulation to (US21).</summary>
public sealed record AgencyResponse(Guid Id, string Name);

[ApiController]
[Route("api/agencies")]
public sealed class AgenciesController(HipoSimDbContext database) : ControllerBase
{
    /// <summary>Public catalogue: only the identifier and the name, sorted by name.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<AgencyResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var agencies = await database.Agencies
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ThenBy(a => a.Id)
            .Select(a => new AgencyResponse(a.Id, a.Name))
            .ToListAsync(cancellationToken);

        return Ok(agencies);
    }
}
