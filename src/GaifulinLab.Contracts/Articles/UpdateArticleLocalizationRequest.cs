namespace GaifulinLab.Contracts.Articles;

public sealed record UpdateArticleLocalizationRequest(
    string? Title,
    string? Summary,
    string? Html,
    string? Slug,
    long? ExpectedVersion = null,
    Guid? CoverMediaAssetId = null);
