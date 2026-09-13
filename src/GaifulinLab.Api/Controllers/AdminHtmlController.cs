using GaifulinLab.Application.Content.RenderHtml;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Content;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/html")]
public sealed class AdminHtmlController(ISender sender) : ControllerBase
{
    [HttpPost("preview")]
    [ProducesResponseType<HtmlPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HtmlPreviewResponse>> Preview(
        HtmlPreviewRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new RenderHtmlQuery(request.Html), cancellationToken));
}
