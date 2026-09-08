namespace GaifulinLab.Contracts.Articles;

public sealed record PublicArticleListItemDto(
    string LanguageCode,
    string Slug,
    string Title,
    string? Summary,
    DateTimeOffset PublishedAt,
    string AuthorDisplayName,
    IReadOnlyList<PublicArticleTaxonomyLinkDto> Topics,
    IReadOnlyList<PublicArticleTaxonomyLinkDto> Series,
    IReadOnlyList<string> Tags,
    Guid? CoverMediaAssetId = null,
    int ReadingMinutes = 1,
    string? SearchSnippet = null,
    string? SearchTitle = null);
