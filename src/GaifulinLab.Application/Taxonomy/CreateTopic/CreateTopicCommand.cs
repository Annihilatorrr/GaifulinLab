using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Application.Taxonomy.CreateTopic;

public sealed record CreateTopicCommand(
    string LanguageCode,
    string Name,
    string Slug,
    string? Description) : IRequest<AdminTopicDto>;
