using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.UpdateArticleLocalization;

internal sealed class UpdateArticleLocalizationCommandHandler(
    IAppDbContext dbContext,
    TimeProvider timeProvider) : IRequestHandler<UpdateArticleLocalizationCommand>
{
    public async Task Handle(UpdateArticleLocalizationCommand request, CancellationToken cancellationToken)
    {
        var article = await dbContext.Articles
            .Include(candidate => candidate.Localizations)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == request.ArticleId
                    && candidate.OwnerUserId == request.UserId
                    && candidate.DeletedAt == null,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Article", request.ArticleId);

        var now = timeProvider.GetUtcNow();
        var localization = article.FindLocalization(request.LanguageCode);
        if (localization is null)
        {
            localization = article.AddLocalization(
                request.LanguageCode,
                now,
                request.Title,
                request.Summary,
                request.Markdown,
                request.Slug);
            dbContext.ArticleLocalizations.Add(localization);
        }
        else
        {
            article.UpdateLocalization(
                request.LanguageCode,
                request.Title,
                request.Summary,
                request.Markdown,
                request.Slug,
                now);
        }

        if (localization.Slug is not null)
        {
            var slugExists = await dbContext.ArticleLocalizations.AnyAsync(
                candidate => candidate.Id != localization.Id
                    && candidate.LanguageCode == localization.LanguageCode
                    && candidate.Slug == localization.Slug,
                cancellationToken);
            if (slugExists)
            {
                throw new RequestConflictException(
                    $"Slug '{localization.Slug}' is already used for language '{localization.LanguageCode}'.");
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
