using GaifulinLab.Api.Configuration;
using GaifulinLab.Application.Common;
using GaifulinLab.Application.Articles.Public.GetPublicArticle;
using GaifulinLab.Application.Media;
using GaifulinLab.Application.Articles.Public.GetPublicArticles;
using GaifulinLab.Application.Taxonomy.Public.GetPublicSeries;
using GaifulinLab.Application.Taxonomy.Public.GetPublicSeriesDetails;
using GaifulinLab.Application.Taxonomy.Public.GetPublicTags;
using GaifulinLab.Application.Taxonomy.Public.GetPublicTopics;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;
using GaifulinLab.Domain.Pdf;
using GaifulinLab.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public")]
public sealed class PublicContentController(
    ISender sender,
    AppDbContext dbContext,
    IMediaStorage mediaStorage) : ControllerBase
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

    [HttpPost("articles/{languageCode}/{slug}/pdf-exports")]
    [EnableRateLimiting(ApiRateLimitPolicies.ArticlePdf)]
    [ProducesResponseType<PdfExportStatusDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PdfExportStatusDto>> CreateArticlePdfExport(
        string languageCode,
        string slug,
        [FromQuery] decimal? lineHeight,
        [FromQuery] decimal? blockSpacing,
        CancellationToken cancellationToken)
    {
        var normalizedLanguageCode = DomainRules.NormalizeLanguageCode(languageCode);
        var normalizedSlug = DomainRules.NormalizeSlug(slug);
        var localization = await dbContext.ArticleLocalizations
            .SingleOrDefaultAsync(candidate =>
                candidate.LanguageCode == normalizedLanguageCode
                && candidate.Slug == normalizedSlug
                && candidate.Status == PublicationStatus.Published,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Published article", $"{normalizedLanguageCode}/{normalizedSlug}");
        var typography = ArticleTypography.FromOptional(lineHeight, blockSpacing);
        var job = PdfExportJob.Create(
            localization,
            typography.LineHeight,
            typography.BlockSpacing,
            DateTimeOffset.UtcNow);
        dbContext.PdfExportJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);

        return AcceptedAtAction(nameof(GetPdfExport), new { job.Id }, ToStatusDto(job));
    }

    [HttpGet("pdf-exports/{id:guid}")]
    [ProducesResponseType<PdfExportStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PdfExportStatusDto>> GetPdfExport(
        Guid id,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.PdfExportJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("PDF export", id.ToString());
        return Ok(ToStatusDto(job));
    }

    [HttpGet("pdf-exports/{id:guid}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadPdfExport(Guid id, CancellationToken cancellationToken)
    {
        var job = await dbContext.PdfExportJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("PDF export", id.ToString());
        if (job.Status != PdfExportStatus.Completed || job.RelativePath is null)
        {
            return Conflict(new ApiErrorResponse("pdf_export_not_ready", "The PDF export is not ready yet."));
        }

        var content = await mediaStorage.OpenReadAsync(job.RelativePath, cancellationToken);
        if (content is null)
        {
            throw new ResourceNotFoundException("PDF export file", id.ToString());
        }

        Response.Headers.CacheControl = "private, no-store";
        return new FileStreamResult(content, "application/pdf")
        {
            FileDownloadName = $"{job.Slug}.pdf"
        };
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

    private PdfExportStatusDto ToStatusDto(PdfExportJob job) =>
        new(
            job.Id,
            job.Status.ToString().ToLowerInvariant(),
            job.ErrorMessage,
            job.Status == PdfExportStatus.Completed
                ? $"/api/public/pdf-exports/{job.Id}/download"
                : null);
}
