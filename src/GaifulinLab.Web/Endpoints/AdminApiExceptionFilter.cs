using GaifulinLab.Application.Common;
using GaifulinLab.Contracts.Common;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Web.Endpoints;

internal sealed class AdminApiExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ResourceNotFoundException exception)
        {
            return Error(StatusCodes.Status404NotFound, "not_found", exception.Message);
        }
        catch (RequestConflictException exception)
        {
            return Error(StatusCodes.Status409Conflict, "conflict", exception.Message);
        }
        catch (DbUpdateException)
        {
            return Error(
                StatusCodes.Status409Conflict,
                "persistence_conflict",
                "The requested change conflicts with existing data.");
        }
        catch (ArgumentException exception)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_operation", exception.Message);
        }
    }

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new ApiErrorResponse(code, message), statusCode: statusCode);
}
