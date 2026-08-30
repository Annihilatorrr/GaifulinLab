using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Application.Taxonomy.Public.GetPublicSeriesDetails;

public sealed record GetPublicSeriesDetailsQuery(
    string LanguageCode,
    string Slug) : IRequest<PublicSeriesDetailsDto>;
