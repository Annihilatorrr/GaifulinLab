namespace GaifulinLab.Contracts.Taxonomy;

public sealed record AdminTopicDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TopicLocalizationDto> Localizations);
