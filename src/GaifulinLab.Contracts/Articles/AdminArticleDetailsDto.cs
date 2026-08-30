namespace GaifulinLab.Contracts.Articles;

public sealed record AdminArticleDetailsDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AdminArticleLocalizationDto> Localizations,
    IReadOnlyList<Guid> TopicIds,
    IReadOnlyList<SeriesAssignmentDto> Series,
    IReadOnlyList<string> Tags);
