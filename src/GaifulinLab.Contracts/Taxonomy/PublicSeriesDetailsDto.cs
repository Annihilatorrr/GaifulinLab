namespace GaifulinLab.Contracts.Taxonomy;

public sealed record PublicSeriesDetailsDto(
    string LanguageCode,
    string Slug,
    string Title,
    string? Description,
    IReadOnlyList<PublicSeriesArticleDto> Articles);
