using GaifulinLab.Application.Common;
using GaifulinLab.Application.Authors;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.Public.GetPublicSeriesDetails;

internal sealed class GetPublicSeriesDetailsQueryHandler(
    IAppDbContext dbContext,
    IAuthorDisplayNameLookup authorDisplayNameLookup)
    : IRequestHandler<GetPublicSeriesDetailsQuery, PublicSeriesDetailsDto>
{
    public async Task<PublicSeriesDetailsDto> Handle(
        GetPublicSeriesDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        var slug = DomainRules.NormalizeSlug(request.Slug);
        var localization = await dbContext.SeriesLocalizations
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.LanguageCode == languageCode
                && candidate.Slug == slug, cancellationToken)
            ?? throw new ResourceNotFoundException("Series", $"{languageCode}/{slug}");
        var rows = await dbContext.ArticleSeries
            .AsNoTracking()
            .Where(link => link.SeriesId == localization.SeriesId)
            .Join(
                dbContext.ArticleLocalizations
                    .AsNoTracking()
                    .Where(articleLocalization => articleLocalization.Article.DeletedAt == null
                        && articleLocalization.LanguageCode == languageCode
                        && articleLocalization.Status == PublicationStatus.Published),
                link => link.ArticleId,
                articleLocalization => articleLocalization.ArticleId,
                (link, articleLocalization) => new
                {
                    link.Position,
                    articleLocalization.Slug,
                    articleLocalization.Title,
                    articleLocalization.Summary,
                    articleLocalization.PublishedAt,
                    articleLocalization.Article.OwnerUserId
                })
            .OrderBy(row => row.Position)
            .ToListAsync(cancellationToken);
        var authorDisplayNames = await authorDisplayNameLookup.GetDisplayNamesAsync(
            rows.Select(row => row.OwnerUserId).ToArray(),
            cancellationToken);

        return new PublicSeriesDetailsDto(
            languageCode,
            localization.Slug,
            localization.Title,
            localization.Description,
            rows.Select(row => new PublicSeriesArticleDto(
                    row.Position,
                    row.Slug!,
                    row.Title,
                    row.Summary,
                    row.PublishedAt!.Value,
                    authorDisplayNames.GetValueOrDefault(row.OwnerUserId, "Author")))
                .ToArray());
    }
}
