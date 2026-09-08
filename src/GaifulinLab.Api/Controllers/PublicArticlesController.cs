using GaifulinLab.Application.Articles.Public.GetPublicArticle;
using GaifulinLab.Application.Articles.Public.GetPublicArticles;
using GaifulinLab.Application.Common;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;
using GaifulinLab.Infrastructure.Analytics;
using GaifulinLab.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public")]
public sealed class PublicArticlesController(
    ISender sender,
    AppDbContext dbContext,
    ArticleViewVisitorHasher articleViewVisitorHasher) : ControllerBase
{
    [HttpGet("search")]
    [ProducesResponseType<ArticleSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ArticleSearchResponse>> Search(
        [FromQuery] ArticleSearchRequest request,
        [FromQuery(Name = "q")] string? query,
        [FromServices] GaifulinLab.Application.Articles.Public.IArticleSearch search,
        CancellationToken cancellationToken)
    {
        request.Query = query ?? request.Query;
        return Ok(await search.SearchAsync(request, cancellationToken));
    }

    [HttpGet("articles")]
    [ProducesResponseType<IReadOnlyList<PublicArticleListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PublicArticleListItemDto>>> GetArticles(
        [FromQuery] string languageCode,
        [FromQuery] string? topic,
        [FromQuery] string? series,
        [FromQuery] string? tag,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new GetPublicArticlesQuery(languageCode, topic, series, tag, page, pageSize),
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
                // EF Core has no provider-agnostic, atomic "insert if absent" operation. Checking
                // first would race when a page is opened in several tabs; PostgreSQL's ON CONFLICT
                // lets the unique constraint decide which request wins and keeps the others harmless.
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
}
