using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Application.Taxonomy.CreateSeries;

public sealed record CreateSeriesCommand(
    string LanguageCode,
    string Title,
    string Slug,
    string? Description) : IRequest<AdminSeriesDto>;
