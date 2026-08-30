namespace GaifulinLab.Contracts.Articles;

public sealed record PublicArticleListItemDto(
    string LanguageCode,
    string Slug,
    string Title,
    string? Summary,
    DateTimeOffset PublishedAt,
    IReadOnlyList<PublicArticleTaxonomyLinkDto> Topics,
    IReadOnlyList<PublicArticleTaxonomyLinkDto> Series,
    IReadOnlyList<string> Tags);
