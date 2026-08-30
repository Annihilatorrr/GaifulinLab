using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Application.Taxonomy.Public.GetPublicTags;

public sealed record GetPublicTagsQuery(string LanguageCode)
    : IRequest<IReadOnlyList<PublicTagDto>>;
