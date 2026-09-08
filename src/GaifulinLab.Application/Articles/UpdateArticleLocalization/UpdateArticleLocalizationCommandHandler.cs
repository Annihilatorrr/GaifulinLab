using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.UpdateArticleLocalization;

internal sealed class UpdateArticleLocalizationCommandHandler(
    IAppDbContext dbContext,
    TimeProvider timeProvider) : IRequestHandler<UpdateArticleLocalizationCommand, long>
{
    public async Task<long> Handle(UpdateArticleLocalizationCommand request, CancellationToken cancellationToken)
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
            if (request.ExpectedVersion is not null)
            {
                throw new RequestConflictException(
                    "The localization no longer exists. Reload the article before saving.",
                    "article_edit_conflict");
            }

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
            if (request.ExpectedVersion is null)
            {
                throw new ArgumentException(
                    "ExpectedVersion is required when updating an existing localization.",
                    nameof(request));
            }

            if (localization.Version != request.ExpectedVersion.Value)
            {
                throw new RequestConflictException(
                    "This localization was changed elsewhere. Your draft was not saved.",
                    "article_edit_conflict");
            }

            article.UpdateLocalization(
                request.LanguageCode,
                request.Title,
                request.Summary,
                request.Markdown,
                request.Slug,
                now);
        }

        if (request.CoverMediaAssetId is { } coverId && !await dbContext.MediaAssets.AnyAsync(
            asset => asset.Id == coverId && asset.ContentType.StartsWith("image/"), cancellationToken))
        {
            throw new ArgumentException("Select an existing image for the article cover.");
        }
        localization.SetCover(request.CoverMediaAssetId);

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

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RequestConflictException(
                "This localization was changed elsewhere. Your draft was not saved.",
                "article_edit_conflict");
        }

        return localization.Version;
    }
}
