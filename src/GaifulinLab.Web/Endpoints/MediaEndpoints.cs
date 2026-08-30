using GaifulinLab.Application.Media.GetMediaFile;
using GaifulinLab.Application.Media.UploadMedia;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Media;
using GaifulinLab.Infrastructure.Authentication;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GaifulinLab.Web.Endpoints;

public static class MediaEndpoints
{
    private const long MaximumRequestSize = MediaUploadLimits.MaximumFileSize + 64 * 1024;

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/media", Upload)
            .WithTags("Admin Media")
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .DisableAntiforgery()
            .AddEndpointFilter<AdminApiExceptionFilter>()
            .WithMetadata(new RequestSizeLimitAttribute(MaximumRequestSize))
            .Produces<UploadMediaResponse>(StatusCodes.Status201Created)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status413PayloadTooLarge);

        endpoints.MapGet("/media/{mediaId:guid}", Get)
            .WithTags("Media")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> Upload(
        IFormFile file,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        var result = await sender.Send(
            new UploadMediaCommand(
                content,
                file.FileName,
                file.ContentType,
                file.Length),
            cancellationToken);

        return Results.Created(result.Url, result);
    }

    private static async Task<IResult> Get(
        Guid mediaId,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetMediaFileQuery(mediaId), cancellationToken);
        if (result is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return Results.Stream(
            result.Content,
            result.ContentType,
            lastModified: result.CreatedAt,
            enableRangeProcessing: true);
    }
}
