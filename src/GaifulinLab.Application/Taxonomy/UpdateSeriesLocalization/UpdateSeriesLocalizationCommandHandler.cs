using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Application.Taxonomy.CreateSeries;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.UpdateSeriesLocalization;

internal sealed class UpdateSeriesLocalizationCommandHandler(IAppDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<UpdateSeriesLocalizationCommand, AdminSeriesDto>
{
    public async Task<AdminSeriesDto> Handle(
        UpdateSeriesLocalizationCommand request,
        CancellationToken cancellationToken)
    {
        CreateSeriesCommandHandler.Validate(request.Title, request.Slug, request.Description);

        var series = await dbContext.Series
            .Include(candidate => candidate.Localizations)
            .Include(candidate => candidate.Articles)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.SeriesId, cancellationToken)
            ?? throw new ResourceNotFoundException("Series", request.SeriesId);

        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        var current = series.FindLocalization(languageCode)
            ?? throw new ResourceNotFoundException("Series localization", languageCode);
        var normalizedSlug = DomainRules.NormalizeSlug(request.Slug);

        var slugExists = await dbContext.SeriesLocalizations.AnyAsync(
            candidate => candidate.Id != current.Id
                && candidate.LanguageCode == languageCode
                && candidate.Slug == normalizedSlug,
            cancellationToken);
        if (slugExists)
        {
            throw CreateSeriesCommandHandler.SlugConflict(languageCode, normalizedSlug);
        }

        series.UpdateLocalization(
            languageCode,
            request.Title,
            normalizedSlug,
            request.Description,
            timeProvider.GetUtcNow());

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw CreateSeriesCommandHandler.SlugConflict(languageCode, normalizedSlug);
        }

        return CreateSeriesCommandHandler.ToDto(series);
    }
}
