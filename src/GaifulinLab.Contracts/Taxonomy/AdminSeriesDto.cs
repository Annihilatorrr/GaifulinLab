namespace GaifulinLab.Contracts.Taxonomy;

public sealed record AdminSeriesDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SeriesLocalizationDto> Localizations,
    IReadOnlyList<AdminSeriesArticleDto> Articles);
