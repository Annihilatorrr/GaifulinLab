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

        article.Delete(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
