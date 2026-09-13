namespace GaifulinLab.Contracts.Articles;

public sealed record CreateArticleRequest(
    string LanguageCode,
    string? Title,
    string? Summary,
    string? Html,
    string? Slug,
    Guid? CoverMediaAssetId = null);
