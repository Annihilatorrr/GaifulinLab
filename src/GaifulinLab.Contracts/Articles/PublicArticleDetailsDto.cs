namespace GaifulinLab.Contracts.Articles;

public sealed record PublicArticleDetailsDto(
    string LanguageCode,
    string Slug,
    string Title,
    string? Summary,
    string Html,
    DateTimeOffset PublishedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AvailableLocalizationDto> AvailableLocalizations,
    IReadOnlyList<PublicArticleTaxonomyLinkDto> Topics,
    IReadOnlyList<PublicArticleTaxonomyLinkDto> Series,
    IReadOnlyList<string> Tags,
    long ViewCount);
