using GaifulinLab.Api.Configuration;
using GaifulinLab.Application.Common;
using GaifulinLab.Application.Media;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;
using GaifulinLab.Domain.Pdf;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin")]
public sealed class AdminPdfExportController(
    AppDbContext dbContext,
    IMediaStorage mediaStorage) : ControllerBase
{
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

    private static PdfExportStatusDto ToStatusDto(PdfExportJob job) =>
        new(
            job.Id,
            job.Status.ToString().ToLowerInvariant(),
            job.ErrorMessage,
            job.Status == PdfExportStatus.Completed
                ? $"/api/admin/pdf-exports/{job.Id}/download"
                : null);

    private Task<PdfExportJob?> GetPublishedPdfExport(Guid id, CancellationToken cancellationToken) =>
        dbContext.PdfExportJobs
            .AsNoTracking()
            .Where(job => job.Id == id
                && dbContext.ArticleLocalizations.Any(localization =>
                    localization.Id == job.ArticleLocalizationId
                    && localization.Article.DeletedAt == null
                    && localization.Status == PublicationStatus.Published))
            .SingleOrDefaultAsync(cancellationToken);
}
