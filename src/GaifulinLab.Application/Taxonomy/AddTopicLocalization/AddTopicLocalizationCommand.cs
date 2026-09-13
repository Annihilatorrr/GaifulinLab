using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Application.Taxonomy.AddTopicLocalization;

public sealed record AddTopicLocalizationCommand(
    Guid TopicId,
    string LanguageCode,
    string Name,
    string Slug,
    string? Description) : IRequest<AdminTopicDto>;
