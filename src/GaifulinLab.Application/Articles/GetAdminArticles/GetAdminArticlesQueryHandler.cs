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
        var articleRows = await dbContext.Articles
            .AsNoTracking()
            // Never load another author's drafts into the workspace list.
            .Where(article => article.OwnerUserId == request.UserId && article.DeletedAt == null)
            .OrderByDescending(article => article.UpdatedAt)
            .Select(article => new
            {
                article.Id,
                article.CreatedAt,
                article.UpdatedAt,
                Localizations = article.Localizations
                    .OrderBy(localization => localization.LanguageCode)
                    .Select(localization => new
                    {
                        localization.Id,
                        localization.LanguageCode,
                        localization.Slug,
                        localization.Title,
                        localization.Status,
                        localization.PublishedAt,
                        localization.UpdatedAt,
                        localization.LastEditedAt
                    })
                    .ToArray()
            })
            .ToListAsync(cancellationToken);

        // The status column is string-backed; casting between enum types in the EF projection
        // makes PostgreSQL try to convert values such as "Draft" to an integer.
        return articleRows
            .Select(article => new AdminArticleListItemDto(
                article.Id,
                article.CreatedAt,
                article.UpdatedAt,
                article.Localizations
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
            .ToArray();
    }
}
