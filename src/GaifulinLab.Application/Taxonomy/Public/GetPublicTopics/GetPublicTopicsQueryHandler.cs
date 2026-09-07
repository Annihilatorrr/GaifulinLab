using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.Public.GetPublicTopics;

internal sealed class GetPublicTopicsQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetPublicTopicsQuery, IReadOnlyList<PublicTopicDto>>
{
    public async Task<IReadOnlyList<PublicTopicDto>> Handle(
        GetPublicTopicsQuery request,
        CancellationToken cancellationToken)
    {
        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        var publishedArticleIds = await (
                from localization in dbContext.ArticleLocalizations.AsNoTracking()
                join article in dbContext.Articles.AsNoTracking()
                    on localization.ArticleId equals article.Id
                where article.DeletedAt == null
                    && localization.LanguageCode == languageCode
                    && localization.Status == PublicationStatus.Published
                select localization.ArticleId)
            .ToListAsync(cancellationToken);
        var links = await dbContext.ArticleTopics
            .AsNoTracking()
            .Where(link => publishedArticleIds.Contains(link.ArticleId))
            .ToListAsync(cancellationToken);
        var counts = links.GroupBy(link => link.TopicId)
            .ToDictionary(group => group.Key, group => group.Count());
        var localizations = await dbContext.TopicLocalizations
            .AsNoTracking()
            .Where(localization => localization.LanguageCode == languageCode)
            .ToListAsync(cancellationToken);

        return localizations
            .Where(localization => counts.ContainsKey(localization.TopicId))
            .Select(localization => new PublicTopicDto(
                languageCode,
                localization.Slug,
                localization.Name,
                localization.Description,
                counts[localization.TopicId]))
            .OrderBy(topic => topic.Name)
            .ToArray();
    }
}
