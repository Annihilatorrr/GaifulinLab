using GaifulinLab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthController(
    AppDbContext dbContext,
    ILogger<HealthController> logger) : ControllerBase
{
    [HttpGet("live")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> GetLiveness() => Ok(new HealthResponse("Healthy"));

    [HttpGet("ready")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> GetReadiness(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? Ok(new HealthResponse("Healthy"))
                : StatusCode(StatusCodes.Status503ServiceUnavailable, new HealthResponse("Unhealthy"));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The database readiness check failed.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new HealthResponse("Unhealthy"));
        }
    }

    public sealed record HealthResponse(string Status);
}
