using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Application.Taxonomy.Public.GetPublicSeries;

public sealed record GetPublicSeriesQuery(string LanguageCode)
    : IRequest<IReadOnlyList<PublicSeriesListItemDto>>;
