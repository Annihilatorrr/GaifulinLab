using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Domain.Articles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.CreateArticle;

internal sealed class CreateArticleCommandHandler(
    IAppDbContext dbContext,
    TimeProvider timeProvider) : IRequestHandler<CreateArticleCommand, CreateArticleResponse>
{
    public async Task<CreateArticleResponse> Handle(
        CreateArticleCommand request,
        CancellationToken cancellationToken)
    {
        var article = Article.Create(
            request.LanguageCode,
            timeProvider.GetUtcNow(),
            request.Title,
            request.Summary,
            request.Markdown,
            request.Slug);
        var localization = article.Localizations.Single();

        await EnsureSlugIsAvailable(localization, cancellationToken);

        dbContext.Articles.Add(article);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateArticleResponse(article.Id, localization.Id);
    }

    private async Task EnsureSlugIsAvailable(
        ArticleLocalization localization,
        CancellationToken cancellationToken)
    {
        if (localization.Slug is null)
        {
            return;
        }

        var exists = await dbContext.ArticleLocalizations.AnyAsync(
            existing => existing.LanguageCode == localization.LanguageCode
                && existing.Slug == localization.Slug,
            cancellationToken);
        if (exists)
        {
            throw new RequestConflictException(
                $"Slug '{localization.Slug}' is already used for language '{localization.LanguageCode}'.");
        }
    }
}
