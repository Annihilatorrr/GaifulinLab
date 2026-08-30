namespace GaifulinLab.Contracts.Taxonomy;

public sealed record PublicTopicDto(
    string LanguageCode,
    string Slug,
    string Name,
    string? Description,
    int ArticleCount);
