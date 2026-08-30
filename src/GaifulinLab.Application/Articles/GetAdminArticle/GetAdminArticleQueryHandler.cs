using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Articles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.GetAdminArticle;

internal sealed class GetAdminArticleQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetAdminArticleQuery, AdminArticleDetailsDto>
{
    public async Task<AdminArticleDetailsDto> Handle(
        GetAdminArticleQuery request,
        CancellationToken cancellationToken)
    {
        var article = await dbContext.Articles
            .AsNoTracking()
            .Include(candidate => candidate.Localizations)
            .Include(candidate => candidate.Topics)
            .Include(candidate => candidate.Tags)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.ArticleId, cancellationToken)
            ?? throw new ResourceNotFoundException("Article", request.ArticleId);

        var tagIds = article.Tags.Select(link => link.TagId).ToArray();
        var tags = await dbContext.Tags
            .AsNoTracking()
            .Where(tag => tagIds.Contains(tag.Id))
            .OrderBy(tag => tag.Name)
            .Select(tag => tag.Name)
            .ToListAsync(cancellationToken);
        var series = await dbContext.ArticleSeries
            .AsNoTracking()
            .Where(link => link.ArticleId == article.Id)
            .OrderBy(link => link.SeriesId)
            .Select(link => new SeriesAssignmentDto(link.SeriesId, link.Position))
            .ToListAsync(cancellationToken);

        return new AdminArticleDetailsDto(
            article.Id,
            article.CreatedAt,
            article.UpdatedAt,
            article.Localizations
                .OrderBy(localization => localization.LanguageCode)
                .Select(localization => localization.ToDetails())
                .ToArray(),
            article.Topics.Select(link => link.TopicId).Order().ToArray(),
            series,
            tags);
    }
}
