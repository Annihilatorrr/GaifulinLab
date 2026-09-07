using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.Public.GetPublicTags;

internal sealed class GetPublicTagsQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetPublicTagsQuery, IReadOnlyList<PublicTagDto>>
{
    public async Task<IReadOnlyList<PublicTagDto>> Handle(
        GetPublicTagsQuery request,
        CancellationToken cancellationToken)
    {
        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        var publishedArticleIds = await dbContext.ArticleLocalizations
            .AsNoTracking()
            .Where(localization => localization.Article.DeletedAt == null
                && localization.LanguageCode == languageCode
                && localization.Status == PublicationStatus.Published)
            .Select(localization => localization.ArticleId)
            .ToListAsync(cancellationToken);
        var links = await dbContext.ArticleTags
            .AsNoTracking()
            .Where(link => publishedArticleIds.Contains(link.ArticleId))
            .ToListAsync(cancellationToken);
        var counts = links.GroupBy(link => link.TagId)
            .ToDictionary(group => group.Key, group => group.Count());
        var tagIds = counts.Keys.ToArray();
        var tags = await dbContext.Tags
            .AsNoTracking()
            .Where(tag => tagIds.Contains(tag.Id))
            .ToListAsync(cancellationToken);

        return tags.Select(tag => new PublicTagDto(tag.Name, counts[tag.Id]))
            .OrderBy(tag => tag.Name)
            .ToArray();
    }
}
