using GaifulinLab.Api.Configuration;
using GaifulinLab.Application.Pdf;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Domain.Common;
using GaifulinLab.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/articles/pdf")]
public sealed class AdminArticlePdfController(IArticlePdfRenderer renderer) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(ApiRateLimitPolicies.ArticlePdf)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Render(
        AdminArticlePdfRequest request,
        CancellationToken cancellationToken)
    {
        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        var typography = ArticleTypography.FromOptional(request.LineHeight, request.BlockSpacing);
        var pdf = await renderer.RenderAsync(
            new ArticlePdfDocument(
                languageCode,
                request.Slug ?? string.Empty,
                request.Title,
                request.Summary,
                request.Markdown,
                request.PublishedAt),
            typography,
            cancellationToken);

        return File(pdf, "application/pdf");
    }
}
