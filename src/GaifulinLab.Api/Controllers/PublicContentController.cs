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
using GaifulinLab.Infrastructure.Analytics;
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
    IMediaStorage mediaStorage,
    ArticleViewVisitorHasher articleViewVisitorHasher) : ControllerBase
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

    [HttpPost("articles/{languageCode}/{slug}/views")]
    [ProducesResponseType<ArticleViewCountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArticleViewCountDto>> RecordArticleView(
        string languageCode,
        string slug,
        CancellationToken cancellationToken)
    {
        var normalizedLanguageCode = DomainRules.NormalizeLanguageCode(languageCode);
        var normalizedSlug = DomainRules.NormalizeSlug(slug);
        // Resolve the requested translation, but record the view against the shared article.
        // Switching language must not turn one reader into two unique visitors.
        var articleId = await dbContext.ArticleLocalizations
            .AsNoTracking()
            .Where(localization =>
                localization.LanguageCode == normalizedLanguageCode
                && localization.Slug == normalizedSlug
                && localization.Status == PublicationStatus.Published
                && dbContext.Articles.Any(article =>
                    article.Id == localization.ArticleId && article.DeletedAt == null))
            .Select(localization => (Guid?)localization.ArticleId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Published article",
                $"{normalizedLanguageCode}/{normalizedSlug}");

        var address = HttpContext.Connection.RemoteIpAddress;
        if (address is not null)
        {
            var visitorHash = articleViewVisitorHasher.Hash(address);
            var firstViewedAt = DateTimeOffset.UtcNow;
            if (dbContext.Database.IsRelational())
            {
                // Checking first would race when a page is opened in several tabs. PostgreSQL
                // decides which request wins, while every other request remains harmless.
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO article_views ("ArticleId", "VisitorHash", "FirstViewedAt")
                    VALUES ({articleId}, {visitorHash}, {firstViewedAt})
                    ON CONFLICT ("ArticleId", "VisitorHash") DO NOTHING;
                    """, cancellationToken);
            }
            else if (!await dbContext.ArticleViews.AnyAsync(
                view => view.ArticleId == articleId && view.VisitorHash == visitorHash,
                cancellationToken))
            {
                // EF's in-memory provider has no ON CONFLICT support; this keeps API tests
                // behaviorally equivalent without changing the production path above.
                dbContext.ArticleViews.Add(ArticleView.Create(articleId, visitorHash, firstViewedAt));
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        var viewCount = await dbContext.ArticleViews
            .LongCountAsync(view => view.ArticleId == articleId, cancellationToken);
        return Ok(new ArticleViewCountDto(viewCount));
    }

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
                && candidate.Status == PublicationStatus.Published
                && dbContext.Articles.Any(article =>
                    article.Id == candidate.ArticleId && article.DeletedAt == null),
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
        var job = await GetPublishedPdfExport(id, cancellationToken)
            ?? throw new ResourceNotFoundException("PDF export", id.ToString());
        return Ok(ToStatusDto(job));
    }

    [HttpGet("pdf-exports/{id:guid}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadPdfExport(Guid id, CancellationToken cancellationToken)
    {
        var job = await GetPublishedPdfExport(id, cancellationToken)
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

    private Task<PdfExportJob?> GetPublishedPdfExport(Guid id, CancellationToken cancellationToken) =>
        (
            from job in dbContext.PdfExportJobs.AsNoTracking()
            join localization in dbContext.ArticleLocalizations.AsNoTracking()
                on job.ArticleLocalizationId equals localization.Id
            join article in dbContext.Articles.AsNoTracking()
                on localization.ArticleId equals article.Id
            where job.Id == id
                && article.DeletedAt == null
                && localization.Status == PublicationStatus.Published
            select job)
        .SingleOrDefaultAsync(cancellationToken);
}
