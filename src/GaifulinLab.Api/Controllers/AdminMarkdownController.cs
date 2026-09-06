using GaifulinLab.Application.Content.RenderMarkdown;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Content;
using GaifulinLab.Infrastructure.Authentication;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/markdown")]
public sealed class AdminMarkdownController(ISender sender) : ControllerBase
{
    [HttpPost("preview")]
    [ProducesResponseType<MarkdownPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MarkdownPreviewResponse>> Preview(
        MarkdownPreviewRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new RenderMarkdownQuery(request.Markdown), cancellationToken));
}
