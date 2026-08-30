using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Web.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new HealthResponse("Healthy")))
            .AllowAnonymous()
            .ExcludeFromDescription();

        endpoints.MapGet("/health/ready", CheckDatabase)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> CheckDatabase(
        AppDbContext dbContext,
        ILogger<AppDbContext> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? Results.Ok(new HealthResponse("Healthy"))
                : Results.Json(new HealthResponse("Unhealthy"), statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The database readiness check failed.");
            return Results.Json(
                new HealthResponse("Unhealthy"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private sealed record HealthResponse(string Status);
}
