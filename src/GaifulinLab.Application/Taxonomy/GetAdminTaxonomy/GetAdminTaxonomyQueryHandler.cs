using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Taxonomy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.GetAdminTaxonomy;

internal sealed class GetAdminTaxonomyQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetAdminTaxonomyQuery, AdminTaxonomyDto>
{
    public async Task<AdminTaxonomyDto> Handle(
        GetAdminTaxonomyQuery request,
        CancellationToken cancellationToken)
    {
        var ownedArticleIds = await dbContext.Articles
            .AsNoTracking()
            .Where(article => article.OwnerUserId == request.UserId && article.DeletedAt == null)
            .Select(article => article.Id)
            .ToArrayAsync(cancellationToken);

        var topics = await dbContext.Topics
            .AsNoTracking()
            .Include(topic => topic.Localizations)
            .OrderBy(topic => topic.CreatedAt)
            .ToListAsync(cancellationToken);
        var series = await dbContext.Series
            .AsNoTracking()
            .Include(item => item.Localizations)
            .Include(item => item.Articles)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        var tags = await dbContext.Tags
            .AsNoTracking()
            .OrderBy(tag => tag.Name)
            .ToListAsync(cancellationToken);

        return new AdminTaxonomyDto(
            topics.Select(topic => new AdminTopicDto(
                    topic.Id,
                    topic.CreatedAt,
                    topic.UpdatedAt,
                    topic.Localizations
                        .OrderBy(localization => localization.LanguageCode)
                        .Select(localization => new TopicLocalizationDto(
                            localization.Id,
                            localization.LanguageCode,
                            localization.Name,
                            localization.Slug,
                            localization.Description))
                        .ToArray()))
                .ToArray(),
            series.Select(item => new AdminSeriesDto(
                    item.Id,
                    item.CreatedAt,
                    item.UpdatedAt,
                    item.Localizations
                        .OrderBy(localization => localization.LanguageCode)
                        .Select(localization => new SeriesLocalizationDto(
                            localization.Id,
                            localization.LanguageCode,
                            localization.Title,
                            localization.Slug,
                            localization.Description))
                        .ToArray(),
                    item.Articles
                        // Series are shared, but the workspace must not expose other authors' drafts.
                        .Where(link => ownedArticleIds.Contains(link.ArticleId))
                        .OrderBy(link => link.Position)
                        .Select(link => new AdminSeriesArticleDto(link.ArticleId, link.Position))
                        .ToArray()))
                .ToArray(),
            tags.Select(tag => new AdminTagDto(tag.Id, tag.Name)).ToArray());
    }
}
