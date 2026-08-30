using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Application.Taxonomy.Public.GetPublicTopics;

public sealed record GetPublicTopicsQuery(string LanguageCode)
    : IRequest<IReadOnlyList<PublicTopicDto>>;
