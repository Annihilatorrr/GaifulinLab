using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.Public.GetPublicSeries;

internal sealed class GetPublicSeriesQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetPublicSeriesQuery, IReadOnlyList<PublicSeriesListItemDto>>
{
    public async Task<IReadOnlyList<PublicSeriesListItemDto>> Handle(
        GetPublicSeriesQuery request,
        CancellationToken cancellationToken)
    {
        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        var publishedArticleIds = await dbContext.ArticleLocalizations
            .AsNoTracking()
            .Where(localization => localization.LanguageCode == languageCode
                && localization.Status == PublicationStatus.Published)
            .Select(localization => localization.ArticleId)
            .ToListAsync(cancellationToken);
        var links = await dbContext.ArticleSeries
            .AsNoTracking()
            .Where(link => publishedArticleIds.Contains(link.ArticleId))
            .ToListAsync(cancellationToken);
        var counts = links.GroupBy(link => link.SeriesId)
            .ToDictionary(group => group.Key, group => group.Count());
        var localizations = await dbContext.SeriesLocalizations
            .AsNoTracking()
            .Where(localization => localization.LanguageCode == languageCode)
            .ToListAsync(cancellationToken);

        return localizations
            .Where(localization => counts.ContainsKey(localization.SeriesId))
            .Select(localization => new PublicSeriesListItemDto(
                languageCode,
                localization.Slug,
                localization.Title,
                localization.Description,
                counts[localization.SeriesId]))
            .OrderBy(series => series.Title)
            .ToArray();
    }
}
