using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.DeleteArticle;

internal sealed class DeleteArticleCommandHandler(IAppDbContext dbContext)
    : IRequestHandler<DeleteArticleCommand>
{
    public async Task Handle(DeleteArticleCommand request, CancellationToken cancellationToken)
    {
        var article = await dbContext.Articles.SingleOrDefaultAsync(
            candidate => candidate.Id == request.ArticleId,
            cancellationToken)
            ?? throw new ResourceNotFoundException("Article", request.ArticleId);

        dbContext.Articles.Remove(article);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
