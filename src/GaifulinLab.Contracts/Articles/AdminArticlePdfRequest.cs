namespace GaifulinLab.Contracts.Articles;

public sealed record AdminArticlePdfRequest(
    string LanguageCode,
    string? Slug,
    string Title,
    string? Summary,
    string Markdown,
    DateTimeOffset? PublishedAt,
    decimal? LineHeight,
    decimal? BlockSpacing);
