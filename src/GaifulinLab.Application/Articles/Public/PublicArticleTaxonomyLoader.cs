using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Articles;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.Public;

public static class PublicArticleTaxonomyLoader
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

        var topics = await dbContext.ArticleTopics
            .AsNoTracking()
            .Where(link => articleIds.Contains(link.ArticleId))
            .Join(
                dbContext.TopicLocalizations
                    .AsNoTracking()
                    .Where(localization => localization.LanguageCode == languageCode),
                link => link.TopicId,
                localization => localization.TopicId,
                (link, localization) => new
                {
                    link.ArticleId,
                    localization.Slug,
                    DisplayName = localization.Name
                })
            .ToListAsync(cancellationToken);

        var series = await dbContext.ArticleSeries
            .AsNoTracking()
            .Where(link => articleIds.Contains(link.ArticleId))
            .Join(
                dbContext.SeriesLocalizations
                    .AsNoTracking()
                    .Where(localization => localization.LanguageCode == languageCode),
                link => link.SeriesId,
                localization => localization.SeriesId,
                (link, localization) => new
                {
                    link.ArticleId,
                    localization.Slug,
                    DisplayName = localization.Title
                })
            .ToListAsync(cancellationToken);

        var tags = await dbContext.ArticleTags
            .AsNoTracking()
            .Where(link => articleIds.Contains(link.ArticleId))
            .Join(
                dbContext.Tags.AsNoTracking(),
                link => link.TagId,
                tag => tag.Id,
                (link, tag) => new { link.ArticleId, tag.Name })
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

public sealed record PublicArticleTaxonomy(
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
