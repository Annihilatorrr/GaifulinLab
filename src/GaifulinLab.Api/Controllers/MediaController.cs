using GaifulinLab.Application.Media.GetMediaFile;
using GaifulinLab.Application.Media.UploadMedia;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Media;
using GaifulinLab.Infrastructure.Authentication;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaifulinLab.Api.Controllers;

[ApiController]
public sealed class MediaController(ISender sender) : ControllerBase
{
    private const long MaximumRequestSize = MediaUploadLimits.MaximumFileSize + 64 * 1024;

    [HttpPost("/api/admin/media")]
    [Authorize]
    [RequestSizeLimit(MaximumRequestSize)]
    [ProducesResponseType<UploadMediaResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<UploadMediaResponse>> Upload(
        IFormFile file,
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

        return Created(result.Url, result);
    }

    [HttpGet("/media/{mediaId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid mediaId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetMediaFileQuery(mediaId), cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return new FileStreamResult(result.Content, result.ContentType)
        {
            LastModified = result.CreatedAt,
            EnableRangeProcessing = true
        };
    }
}
