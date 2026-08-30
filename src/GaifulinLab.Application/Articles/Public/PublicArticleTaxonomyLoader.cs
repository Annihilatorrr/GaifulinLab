using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Articles;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.Public;

internal static class PublicArticleTaxonomyLoader
{
    public static async Task<PublicArticleTaxonomy> Load(
        IAppDbContext dbContext,
        IReadOnlyCollection<Guid> articleIds,
        string languageCode,
        CancellationToken cancellationToken)
    {
        if (articleIds.Count == 0)
        {
            return PublicArticleTaxonomy.Empty;
        }

        var topics = await (
                from link in dbContext.ArticleTopics.AsNoTracking()
                join localization in dbContext.TopicLocalizations.AsNoTracking()
                    on link.TopicId equals localization.TopicId
                where articleIds.Contains(link.ArticleId)
                    && localization.LanguageCode == languageCode
                select new
                {
                    link.ArticleId,
                    localization.Slug,
                    DisplayName = localization.Name
                })
            .ToListAsync(cancellationToken);

        var series = await (
                from link in dbContext.ArticleSeries.AsNoTracking()
                join localization in dbContext.SeriesLocalizations.AsNoTracking()
                    on link.SeriesId equals localization.SeriesId
                where articleIds.Contains(link.ArticleId)
                    && localization.LanguageCode == languageCode
                select new
                {
                    link.ArticleId,
                    localization.Slug,
                    DisplayName = localization.Title
                })
            .ToListAsync(cancellationToken);

        var tags = await (
                from link in dbContext.ArticleTags.AsNoTracking()
                join tag in dbContext.Tags.AsNoTracking() on link.TagId equals tag.Id
                where articleIds.Contains(link.ArticleId)
                select new { link.ArticleId, tag.Name })
            .ToListAsync(cancellationToken);

        return new PublicArticleTaxonomy(
            topics.GroupBy(item => item.ArticleId).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PublicArticleTaxonomyLinkDto>)group
                    .OrderBy(item => item.DisplayName)
                    .Select(item => new PublicArticleTaxonomyLinkDto(item.Slug, item.DisplayName))
                    .ToArray()),
            series.GroupBy(item => item.ArticleId).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PublicArticleTaxonomyLinkDto>)group
                    .OrderBy(item => item.DisplayName)
                    .Select(item => new PublicArticleTaxonomyLinkDto(item.Slug, item.DisplayName))
                    .ToArray()),
            tags.GroupBy(item => item.ArticleId).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderBy(item => item.Name)
                    .Select(item => item.Name)
                    .ToArray()));
    }
}

internal sealed record PublicArticleTaxonomy(
    IReadOnlyDictionary<Guid, IReadOnlyList<PublicArticleTaxonomyLinkDto>> Topics,
    IReadOnlyDictionary<Guid, IReadOnlyList<PublicArticleTaxonomyLinkDto>> Series,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>> Tags)
{
    public static PublicArticleTaxonomy Empty { get; } = new(
        new Dictionary<Guid, IReadOnlyList<PublicArticleTaxonomyLinkDto>>(),
        new Dictionary<Guid, IReadOnlyList<PublicArticleTaxonomyLinkDto>>(),
        new Dictionary<Guid, IReadOnlyList<string>>());

    public IReadOnlyList<PublicArticleTaxonomyLinkDto> TopicsFor(Guid articleId) =>
        Topics.GetValueOrDefault(articleId) ?? [];

    public IReadOnlyList<PublicArticleTaxonomyLinkDto> SeriesFor(Guid articleId) =>
        Series.GetValueOrDefault(articleId) ?? [];

    public IReadOnlyList<string> TagsFor(Guid articleId) =>
        Tags.GetValueOrDefault(articleId) ?? [];
}
