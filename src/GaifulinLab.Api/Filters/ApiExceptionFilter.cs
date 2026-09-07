using GaifulinLab.Application.Common;
using GaifulinLab.Contracts.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Api.Filters;

internal sealed class ApiExceptionFilter : IAsyncExceptionFilter
{
    public Task OnExceptionAsync(ExceptionContext context)
    {
        var (statusCode, code, message) = context.Exception switch
        {
            ResourceNotFoundException exception =>
                (StatusCodes.Status404NotFound, "not_found", exception.Message),
            RequestConflictException exception =>
                (StatusCodes.Status409Conflict, exception.Code, exception.Message),
            PdfRenderingException exception =>
                (StatusCodes.Status503ServiceUnavailable, "pdf_renderer_unavailable", exception.Message),
            DbUpdateException =>
                (
                    StatusCodes.Status409Conflict,
                    "persistence_conflict",
                    "The requested change conflicts with existing data."),
            ArgumentException exception =>
                (StatusCodes.Status400BadRequest, "invalid_request", exception.Message),
            InvalidOperationException exception =>
                (StatusCodes.Status400BadRequest, "invalid_operation", exception.Message),
            _ => default
        };

        if (statusCode == default)
        {
            return Task.CompletedTask;
        }

        context.Result = new ObjectResult(new ApiErrorResponse(code, message))
        {
            StatusCode = statusCode
        };
        context.ExceptionHandled = true;

        return Task.CompletedTask;
    }
}
