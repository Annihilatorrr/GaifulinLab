using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Application.Taxonomy.CreateSeries;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.AddSeriesLocalization;

internal sealed class AddSeriesLocalizationCommandHandler(
    IAppDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<AddSeriesLocalizationCommand, AdminSeriesDto>
{
    public async Task<AdminSeriesDto> Handle(
        AddSeriesLocalizationCommand request,
        CancellationToken cancellationToken)
    {
        CreateSeriesCommandHandler.Validate(request.Title, request.Slug, request.Description);

        var series = await dbContext.Series
            .Include(candidate => candidate.Localizations)
            .Include(candidate => candidate.Articles)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.SeriesId, cancellationToken)
            ?? throw new ResourceNotFoundException("Series", request.SeriesId);

        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        if (series.FindLocalization(languageCode) is not null)
        {
            throw LocalizationConflict("series", languageCode);
        }

        var normalizedSlug = DomainRules.NormalizeSlug(request.Slug);
        if (await dbContext.SeriesLocalizations.AnyAsync(
                candidate => candidate.LanguageCode == languageCode && candidate.Slug == normalizedSlug,
                cancellationToken))
        {
            throw CreateSeriesCommandHandler.SlugConflict(languageCode, normalizedSlug);
        }

        var localization = series.AddLocalization(
            languageCode,
            request.Title,
            normalizedSlug,
            request.Description,
            timeProvider.GetUtcNow());
        dbContext.SeriesLocalizations.Add(localization);

        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateSeriesCommandHandler.ToDto(series);
    }

    private static RequestConflictException LocalizationConflict(string taxonomyName, string languageCode) =>
        new($"The {taxonomyName} already has a '{languageCode}' localization.", "taxonomy_localization_conflict");
}
