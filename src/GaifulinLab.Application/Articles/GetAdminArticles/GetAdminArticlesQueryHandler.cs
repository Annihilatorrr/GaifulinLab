using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Articles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.GetAdminArticles;

internal sealed class GetAdminArticlesQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetAdminArticlesQuery, AdminArticleListResponse>
{
    private const int MaximumPageSize = 100;

    public async Task<AdminArticleListResponse> Handle(
        GetAdminArticlesQuery request,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize);
        var baseQuery = dbContext.Articles
            .AsNoTracking()
            // Never load another author's drafts into the workspace list.
            .Where(article => article.OwnerUserId == request.UserId && article.DeletedAt == null);
        var totalCount = await baseQuery.LongCountAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);
        var page = totalPages == 0
            ? 1
            : Math.Min(Math.Max(request.Page, 1), totalPages);

        var articleRows = await baseQuery
            .OrderByDescending(article => article.UpdatedAt)
            .ThenByDescending(article => article.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
        var items = articleRows
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

        return new AdminArticleListResponse(items, totalCount, page, pageSize, totalPages);
    }
}
