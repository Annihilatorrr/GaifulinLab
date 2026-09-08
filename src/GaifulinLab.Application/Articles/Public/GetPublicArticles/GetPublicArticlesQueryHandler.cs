using GaifulinLab.Application.Persistence;
using GaifulinLab.Application.Authors;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.Public.GetPublicArticles;

internal sealed class GetPublicArticlesQueryHandler(
    IAppDbContext dbContext,
    IAuthorDisplayNameLookup authorDisplayNameLookup)
    : IRequestHandler<GetPublicArticlesQuery, IReadOnlyList<PublicArticleListItemDto>>
{
    private const int MaximumPageSize = 100;

    public async Task<IReadOnlyList<PublicArticleListItemDto>> Handle(
        GetPublicArticlesQuery request,
        CancellationToken cancellationToken)
    {
        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        var topicSlug = DomainRules.NormalizeOptionalSlug(request.TopicSlug);
        var seriesSlug = DomainRules.NormalizeOptionalSlug(request.SeriesSlug);
        var normalizedTag = string.IsNullOrWhiteSpace(request.Tag)
            ? null
            : request.Tag.Trim().ToLowerInvariant();
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize);

        var query = dbContext.ArticleLocalizations
            .AsNoTracking()
            .Where(localization =>
                localization.LanguageCode == languageCode
                && localization.Status == PublicationStatus.Published
                && dbContext.Articles.Any(article =>
                    article.Id == localization.ArticleId && article.DeletedAt == null));

        if (topicSlug is not null)
        {
            query = query.Where(localization => dbContext.ArticleTopics.Any(link =>
                link.ArticleId == localization.ArticleId &&
                dbContext.TopicLocalizations.Any(localization =>
                    localization.TopicId == link.TopicId
                    && localization.LanguageCode == languageCode
                    && localization.Slug == topicSlug)));
        }

        if (seriesSlug is not null)
        {
            query = query.Where(localization => dbContext.ArticleSeries.Any(link =>
                link.ArticleId == localization.ArticleId
                && dbContext.SeriesLocalizations.Any(localization =>
                    localization.SeriesId == link.SeriesId
                    && localization.LanguageCode == languageCode
                    && localization.Slug == seriesSlug)));
        }

        if (normalizedTag is not null)
        {
            query = query.Where(localization => dbContext.ArticleTags.Any(link =>
                link.ArticleId == localization.ArticleId &&
                dbContext.Tags.Any(tag =>
                    tag.Id == link.TagId && tag.NormalizedName == normalizedTag)));
        }

        var articles = await query
            .OrderByDescending(localization => localization.PublishedAt)
            .ThenByDescending(localization => localization.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(localization => new
            {
                localization.ArticleId,
                OwnerUserId = localization.Article.OwnerUserId,
                Slug = localization.Slug!,
                localization.Title,
                localization.Summary,
                localization.CoverMediaAssetId,
                localization.ReadingMinutes,
                PublishedAt = localization.PublishedAt!.Value
            })
            .ToListAsync(cancellationToken);
        var taxonomy = await PublicArticleTaxonomyLoader.Load(
            dbContext,
            articles.Select(article => article.ArticleId).ToArray(),
            languageCode,
            cancellationToken);
        var authorDisplayNames = await authorDisplayNameLookup.GetDisplayNamesAsync(
            articles.Select(article => article.OwnerUserId).ToArray(),
            cancellationToken);

        return articles
            .Select(article => new PublicArticleListItemDto(
                languageCode,
                article.Slug,
                article.Title,
                article.Summary,
                article.PublishedAt,
                authorDisplayNames.GetValueOrDefault(article.OwnerUserId, "Author"),
                taxonomy.TopicsFor(article.ArticleId),
                taxonomy.SeriesFor(article.ArticleId),
                taxonomy.TagsFor(article.ArticleId),
                article.CoverMediaAssetId,
                article.ReadingMinutes))
            .ToArray();
    }
}
