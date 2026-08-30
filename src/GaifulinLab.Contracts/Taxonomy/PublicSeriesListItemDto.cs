namespace GaifulinLab.Contracts.Taxonomy;

public sealed record PublicSeriesListItemDto(
    string LanguageCode,
    string Slug,
    string Title,
    string? Description,
    int ArticleCount);
