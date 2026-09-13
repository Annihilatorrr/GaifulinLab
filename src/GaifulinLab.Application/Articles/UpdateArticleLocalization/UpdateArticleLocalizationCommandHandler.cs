using GaifulinLab.Application.Common;
using GaifulinLab.Application.Content;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GaifulinLab.Application.Articles.UpdateArticleLocalization;

internal sealed class UpdateArticleLocalizationCommandHandler(
    IAppDbContext dbContext,
    TimeProvider timeProvider,
    IArticleHtmlSanitizer htmlSanitizer,
    ILogger<UpdateArticleLocalizationCommandHandler> logger) : IRequestHandler<UpdateArticleLocalizationCommand, long>
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

        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        var now = timeProvider.GetUtcNow();
        var localization = article.FindLocalization(languageCode);
        // Conflict diagnostics contain identifiers and versions only, never draft content or user data.
        if (localization is null)
        {
            if (request.ExpectedVersion is not null)
            {
                logger.LogWarning(
                    "Article localization update conflict. Reason: {ConflictReason}; ArticleId: {ArticleId}; LanguageCode: {LanguageCode}; ExpectedLocalizationVersion: {ExpectedLocalizationVersion}; ObservedLocalizationVersion: {ObservedLocalizationVersion}; ArticleVersion: {ArticleVersion}",
                    "missing_localization",
                    article.Id,
                    languageCode,
                    request.ExpectedVersion,
                    null,
                    article.Version);
                throw new RequestConflictException(
                    "The localization no longer exists. Reload the article before saving.",
                    "article_edit_conflict");
            }

            localization = article.AddLocalization(
                languageCode,
                now,
                request.Title,
                request.Summary,
                htmlSanitizer.Sanitize(request.Html ?? string.Empty),
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
                logger.LogWarning(
                    "Article localization update conflict. Reason: {ConflictReason}; ArticleId: {ArticleId}; LanguageCode: {LanguageCode}; ExpectedLocalizationVersion: {ExpectedLocalizationVersion}; ObservedLocalizationVersion: {ObservedLocalizationVersion}; ArticleVersion: {ArticleVersion}",
                    "version_mismatch",
                    article.Id,
                    languageCode,
                    request.ExpectedVersion,
                    localization.Version,
                    article.Version);
                throw new RequestConflictException(
                    "This localization was changed elsewhere. Your draft was not saved.",
                    "article_edit_conflict");
            }

            article.UpdateLocalization(
                languageCode,
                request.Title,
                request.Summary,
                htmlSanitizer.Sanitize(request.Html ?? string.Empty),
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
            logger.LogWarning(
                "Article localization save conflict. ArticleId: {ArticleId}; LanguageCode: {LanguageCode}",
                article.Id,
                languageCode);
            throw new RequestConflictException(
                "This localization was changed elsewhere. Your draft was not saved.",
                "article_edit_conflict");
        }

        return localization.Version;
    }
}
