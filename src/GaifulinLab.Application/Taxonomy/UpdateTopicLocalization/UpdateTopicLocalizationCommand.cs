using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Application.Taxonomy.UpdateTopicLocalization;

public sealed record UpdateTopicLocalizationCommand(
    Guid TopicId,
    string LanguageCode,
    string Name,
    string Slug,
    string? Description) : IRequest<AdminTopicDto>;
