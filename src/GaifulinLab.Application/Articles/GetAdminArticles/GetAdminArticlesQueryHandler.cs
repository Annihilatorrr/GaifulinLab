using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Articles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.GetAdminArticles;

internal sealed class GetAdminArticlesQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetAdminArticlesQuery, IReadOnlyList<AdminArticleListItemDto>>
{
    public async Task<IReadOnlyList<AdminArticleListItemDto>> Handle(
        GetAdminArticlesQuery request,
        CancellationToken cancellationToken)
    {
        var articles = await dbContext.Articles
            .AsNoTracking()
            // Never load another author's drafts into the workspace list.
            .Where(article => article.OwnerUserId == request.UserId && article.DeletedAt == null)
            .OrderByDescending(article => article.UpdatedAt)
            .Select(article => new AdminArticleListItemDto(
                article.Id,
                article.CreatedAt,
                article.UpdatedAt,
                article.Localizations
                    .OrderBy(localization => localization.LanguageCode)
                    .Select(localization => new AdminArticleLocalizationSummaryDto(
                        localization.Id,
                        localization.LanguageCode,
                        localization.Slug,
                        localization.Title,
                        (PublicationStatusDto)localization.Status,
                        localization.PublishedAt,
                        localization.UpdatedAt,
                        localization.LastEditedAt))
                    .ToArray()))
            .ToListAsync(cancellationToken);

        return articles;
    }
}
