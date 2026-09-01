using GaifulinLab.Api.Configuration;
using GaifulinLab.Application.Articles.Public.GetPublicArticle;
using GaifulinLab.Application.Pdf;
using GaifulinLab.Application.Articles.Public.GetPublicArticles;
using GaifulinLab.Application.Taxonomy.Public.GetPublicSeries;
using GaifulinLab.Application.Taxonomy.Public.GetPublicSeriesDetails;
using GaifulinLab.Application.Taxonomy.Public.GetPublicTags;
using GaifulinLab.Application.Taxonomy.Public.GetPublicTopics;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Taxonomy;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public")]
public sealed class PublicContentController(
    ISender sender,
    IArticlePdfRenderer articlePdfRenderer) : ControllerBase
{
    [HttpGet("articles")]
    [ProducesResponseType<IReadOnlyList<PublicArticleListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PublicArticleListItemDto>>> GetArticles(
        [FromQuery] string languageCode,
        [FromQuery] string? topic,
        [FromQuery] string? series,
        [FromQuery] string? tag,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new GetPublicArticlesQuery(languageCode, topic, series, tag),
            cancellationToken));

    [HttpGet("articles/{languageCode}/{slug}")]
    [ProducesResponseType<PublicArticleDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicArticleDetailsDto>> GetArticle(
        string languageCode,
        string slug,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPublicArticleQuery(languageCode, slug), cancellationToken));

    [HttpGet("articles/{languageCode}/{slug}/pdf")]
    [EnableRateLimiting(ApiRateLimitPolicies.ArticlePdf)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadArticlePdf(
        string languageCode,
        string slug,
        [FromQuery] decimal? lineHeight,
        [FromQuery] decimal? blockSpacing,
        CancellationToken cancellationToken)
    {
        var article = await sender.Send(
            new GetPublicArticleQuery(languageCode, slug),
            cancellationToken);
        var pdf = await articlePdfRenderer.RenderAsync(
            article.LanguageCode,
            article.Slug,
            ArticleTypography.FromOptional(lineHeight, blockSpacing),
            cancellationToken);

        Response.Headers.CacheControl = "private, no-store";
        return File(pdf, "application/pdf", $"{article.Slug}.pdf");
    }

    [HttpGet("topics/{languageCode}")]
    [ProducesResponseType<IReadOnlyList<PublicTopicDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PublicTopicDto>>> GetTopics(
        string languageCode,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPublicTopicsQuery(languageCode), cancellationToken));

    [HttpGet("series/{languageCode}")]
    [ProducesResponseType<IReadOnlyList<PublicSeriesListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PublicSeriesListItemDto>>> GetSeries(
        string languageCode,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPublicSeriesQuery(languageCode), cancellationToken));

    [HttpGet("series/{languageCode}/{slug}")]
    [ProducesResponseType<PublicSeriesDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicSeriesDetailsDto>> GetSeriesDetails(
        string languageCode,
        string slug,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new GetPublicSeriesDetailsQuery(languageCode, slug),
            cancellationToken));

    [HttpGet("tags/{languageCode}")]
    [ProducesResponseType<IReadOnlyList<PublicTagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PublicTagDto>>> GetTags(
        string languageCode,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPublicTagsQuery(languageCode), cancellationToken));
}
