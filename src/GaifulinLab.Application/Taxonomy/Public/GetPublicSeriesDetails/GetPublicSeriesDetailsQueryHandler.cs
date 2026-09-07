using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.Public.GetPublicSeriesDetails;

internal sealed class GetPublicSeriesDetailsQueryHandler(IAppDbContext dbContext)
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
        var rows = await (
                from link in dbContext.ArticleSeries.AsNoTracking()
                join articleLocalization in dbContext.ArticleLocalizations.AsNoTracking()
                    on link.ArticleId equals articleLocalization.ArticleId
                join article in dbContext.Articles.AsNoTracking()
                    on link.ArticleId equals article.Id
                where link.SeriesId == localization.SeriesId
                    && article.DeletedAt == null
                    && articleLocalization.LanguageCode == languageCode
                    && articleLocalization.Status == PublicationStatus.Published
                orderby link.Position
                select new
                {
                    link.Position,
                    articleLocalization.Slug,
                    articleLocalization.Title,
                    articleLocalization.Summary,
                    articleLocalization.PublishedAt
                })
            .ToListAsync(cancellationToken);

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
                    row.PublishedAt!.Value))
                .ToArray());
    }
}
