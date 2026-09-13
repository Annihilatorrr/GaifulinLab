using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Application.Taxonomy.AddSeriesLocalization;

public sealed record AddSeriesLocalizationCommand(
    Guid SeriesId,
    string LanguageCode,
    string Title,
    string Slug,
    string? Description) : IRequest<AdminSeriesDto>;
