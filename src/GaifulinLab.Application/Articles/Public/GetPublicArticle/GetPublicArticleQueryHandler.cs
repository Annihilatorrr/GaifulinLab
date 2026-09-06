using GaifulinLab.Application.Common;
using GaifulinLab.Application.Content;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.Public.GetPublicArticle;

internal sealed class GetPublicArticleQueryHandler(
    IAppDbContext dbContext,
    IMarkdownRenderer markdownRenderer) : IRequestHandler<GetPublicArticleQuery, PublicArticleDetailsDto>
{
    public async Task<PublicArticleDetailsDto> Handle(
        GetPublicArticleQuery request,
        CancellationToken cancellationToken)
    {
        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        var slug = DomainRules.NormalizeSlug(request.Slug);
        var article = await dbContext.Articles
            .AsNoTracking()
            .Include(candidate => candidate.Localizations)
            .SingleOrDefaultAsync(candidate => candidate.Localizations.Any(localization =>
                localization.LanguageCode == languageCode
                && localization.Slug == slug
                && localization.Status == PublicationStatus.Published), cancellationToken)
            ?? throw new ResourceNotFoundException("Published article", $"{languageCode}/{slug}");
        var localization = article.Localizations.Single(item =>
            item.LanguageCode == languageCode
            && item.Slug == slug
            && item.Status == PublicationStatus.Published);
        var taxonomy = await PublicArticleTaxonomyLoader.Load(
            dbContext,
            [article.Id],
            languageCode,
            cancellationToken);
        var viewCount = await dbContext.ArticleViews
            .LongCountAsync(view => view.ArticleId == article.Id, cancellationToken);

        return new PublicArticleDetailsDto(
            languageCode,
            localization.Slug!,
            localization.Title,
            localization.Summary,
            markdownRenderer.Render(localization.Markdown),
            localization.PublishedAt!.Value,
            localization.UpdatedAt,
            article.Localizations
                .Where(item => item.Status == PublicationStatus.Published)
                .OrderBy(item => item.LanguageCode)
                .Select(item => new AvailableLocalizationDto(
                    item.LanguageCode,
                    $"/{item.LanguageCode}/articles/{item.Slug}"))
                .ToArray(),
            taxonomy.TopicsFor(article.Id),
            taxonomy.SeriesFor(article.Id),
            taxonomy.TagsFor(article.Id),
            viewCount);
    }
}
