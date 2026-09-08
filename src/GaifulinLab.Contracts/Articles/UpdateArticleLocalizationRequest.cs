namespace GaifulinLab.Contracts.Articles;

public sealed record UpdateArticleLocalizationRequest(
    string? Title,
    string? Summary,
    string? Markdown,
    string? Slug,
    long? ExpectedVersion = null,
    Guid? CoverMediaAssetId = null);
