namespace GaifulinLab.Contracts.Articles;

public sealed record AdminArticleLocalizationSummaryDto(
    Guid Id,
    string LanguageCode,
    string? Slug,
    string Title,
    PublicationStatusDto Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset UpdatedAt);
