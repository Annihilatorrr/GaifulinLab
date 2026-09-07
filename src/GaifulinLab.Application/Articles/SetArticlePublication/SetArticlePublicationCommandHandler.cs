using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Domain.Articles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.SetArticlePublication;

internal sealed class SetArticlePublicationCommandHandler(
    IAppDbContext dbContext,
    TimeProvider timeProvider) : IRequestHandler<SetArticlePublicationCommand>
{
    public async Task Handle(SetArticlePublicationCommand request, CancellationToken cancellationToken)
    {
        var article = await dbContext.Articles
            .Include(candidate => candidate.Localizations)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == request.ArticleId
                    && candidate.OwnerUserId == request.UserId
                    && candidate.DeletedAt == null,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Article", request.ArticleId);

        if (article.FindLocalization(request.LanguageCode) is null)
        {
            throw new ResourceNotFoundException("Article localization", request.LanguageCode);
        }

        var now = timeProvider.GetUtcNow();
        if (request.TargetStatus == PublicationStatus.Published)
        {
            article.PublishLocalization(request.LanguageCode, now);
        }
        else if (request.TargetStatus == PublicationStatus.Unpublished)
        {
            article.UnpublishLocalization(request.LanguageCode, now);
        }
        else
        {
            throw new ArgumentException("The target status must be Published or Unpublished.", nameof(request));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
