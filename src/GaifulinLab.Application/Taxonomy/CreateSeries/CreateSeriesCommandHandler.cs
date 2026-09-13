using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Common;
using SeriesAggregate = GaifulinLab.Domain.Series.Series;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.CreateSeries;

internal sealed class CreateSeriesCommandHandler(IAppDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<CreateSeriesCommand, AdminSeriesDto>
{
    public async Task<AdminSeriesDto> Handle(CreateSeriesCommand request, CancellationToken cancellationToken)
    {
        Validate(request.Title, request.Slug, request.Description);

        var series = SeriesAggregate.Create(
            request.LanguageCode,
            request.Title,
            request.Slug,
            request.Description,
            timeProvider.GetUtcNow());
        var localization = series.Localizations.Single();

        await EnsureSlugIsAvailable(localization.LanguageCode, localization.Slug, null, cancellationToken);
        dbContext.Series.Add(series);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw SlugConflict(localization.LanguageCode, localization.Slug);
        }

        return ToDto(series);
    }

    private async Task EnsureSlugIsAvailable(
        string languageCode,
        string slug,
        Guid? excludedLocalizationId,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.SeriesLocalizations.AnyAsync(
            candidate => candidate.LanguageCode == languageCode
                && candidate.Slug == slug
                && candidate.Id != excludedLocalizationId,
            cancellationToken);
        if (exists)
        {
            throw SlugConflict(languageCode, slug);
        }
    }

    internal static void Validate(string title, string slug, string? description)
    {
        DomainRules.EnsureMaximumLength(title, 300, nameof(title));
        DomainRules.EnsureMaximumLength(slug, 200, nameof(slug));
        DomainRules.EnsureMaximumLength(description, 2_000, nameof(description));
    }

    internal static RequestConflictException SlugConflict(string languageCode, string slug) =>
        new($"Slug '{slug}' is already used for language '{languageCode}'.", "taxonomy_slug_conflict");

    internal static AdminSeriesDto ToDto(SeriesAggregate series) => new(
        series.Id,
        series.CreatedAt,
        series.UpdatedAt,
        series.Localizations
            .OrderBy(localization => localization.LanguageCode)
            .Select(localization => new SeriesLocalizationDto(
                localization.Id,
                localization.LanguageCode,
                localization.Title,
                localization.Slug,
                localization.Description))
            .ToArray(),
        series.Articles
            .OrderBy(article => article.Position)
            .Select(article => new AdminSeriesArticleDto(article.ArticleId, article.Position))
            .ToArray());
}
