namespace GaifulinLab.Contracts.Taxonomy;

public sealed record PublicSeriesArticleDto(
    int Position,
    string Slug,
    string Title,
    string? Summary,
    DateTimeOffset PublishedAt);
