namespace GaifulinLab.Contracts.Articles;

public sealed record CreateArticleRequest(
    string LanguageCode,
    string? Title,
    string? Summary,
    string? Markdown,
    string? Slug);
