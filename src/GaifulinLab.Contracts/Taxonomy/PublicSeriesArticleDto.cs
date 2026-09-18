using GaifulinLab.Contracts.Articles;

namespace GaifulinLab.Contracts.Taxonomy;

public sealed record PublicSeriesArticleDto(
    int Position,
    string Slug,
    string Title,
    string? Summary,
    DateTimeOffset PublishedAt,
    IReadOnlyList<PublicArticleTaxonomyLinkDto>? Topics = null,
    IReadOnlyList<string>? Tags = null,
    Guid? CoverMediaAssetId = null,
    int ReadingMinutes = 1,
    DateTimeOffset? LastEditedAt = null);
