using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.Public.GetPublicArticles;

internal sealed class GetPublicArticlesQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetPublicArticlesQuery, IReadOnlyList<PublicArticleListItemDto>>
{
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

        var query = dbContext.Articles
            .AsNoTracking()
            .Where(article => article.DeletedAt == null && article.Localizations.Any(localization =>
                localization.LanguageCode == languageCode
                && localization.Status == PublicationStatus.Published));

        if (topicSlug is not null)
        {
            query = query.Where(article => article.Topics.Any(link =>
                dbContext.TopicLocalizations.Any(localization =>
                    localization.TopicId == link.TopicId
                    && localization.LanguageCode == languageCode
                    && localization.Slug == topicSlug)));
        }

        if (seriesSlug is not null)
        {
            query = query.Where(article => dbContext.ArticleSeries.Any(link =>
                link.ArticleId == article.Id
                && dbContext.SeriesLocalizations.Any(localization =>
                    localization.SeriesId == link.SeriesId
                    && localization.LanguageCode == languageCode
                    && localization.Slug == seriesSlug)));
        }

        if (normalizedTag is not null)
        {
            query = query.Where(article => article.Tags.Any(link =>
                dbContext.Tags.Any(tag =>
                    tag.Id == link.TagId && tag.NormalizedName == normalizedTag)));
        }

        var articles = await query
            .Include(article => article.Localizations)
            .ToListAsync(cancellationToken);
        var taxonomy = await PublicArticleTaxonomyLoader.Load(
            dbContext,
            articles.Select(article => article.Id).ToArray(),
            languageCode,
            cancellationToken);

        return articles
            .Select(article =>
            {
                var localization = article.Localizations.Single(item =>
                    item.LanguageCode == languageCode
                    && item.Status == PublicationStatus.Published);
                return new PublicArticleListItemDto(
                    languageCode,
                    localization.Slug!,
                    localization.Title,
                    localization.Summary,
                    localization.PublishedAt!.Value,
                    taxonomy.TopicsFor(article.Id),
                    taxonomy.SeriesFor(article.Id),
                    taxonomy.TagsFor(article.Id));
            })
            .OrderByDescending(article => article.PublishedAt)
            .ToArray();
    }
}
