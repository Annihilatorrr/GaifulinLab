using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.DeleteArticle;

internal sealed class DeleteArticleCommandHandler(
    IAppDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<DeleteArticleCommand>
{
    public async Task Handle(DeleteArticleCommand request, CancellationToken cancellationToken)
    {
        var article = await dbContext.Articles.SingleOrDefaultAsync(
            candidate => candidate.Id == request.ArticleId
                && candidate.OwnerUserId == request.UserId
                && candidate.DeletedAt == null,
            cancellationToken)
            ?? throw new ResourceNotFoundException("Article", request.ArticleId);

        var now = timeProvider.GetUtcNow();
        var series = await dbContext.Series
            .Include(candidate => candidate.Articles)
            .Where(candidate => candidate.Articles.Any(link => link.ArticleId == article.Id))
            .ToListAsync(cancellationToken);

        foreach (var item in series)
        {
            item.RemoveArticle(article.Id, now);
        }

        article.Delete(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
