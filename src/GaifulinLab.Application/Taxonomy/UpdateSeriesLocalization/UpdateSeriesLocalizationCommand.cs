using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Application.Taxonomy.UpdateSeriesLocalization;

public sealed record UpdateSeriesLocalizationCommand(
    Guid SeriesId,
    string LanguageCode,
    string Title,
    string Slug,
    string? Description) : IRequest<AdminSeriesDto>;
