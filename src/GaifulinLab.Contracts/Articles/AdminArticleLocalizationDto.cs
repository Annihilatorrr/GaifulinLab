namespace GaifulinLab.Contracts.Articles;

public sealed record AdminArticleLocalizationDto(
    Guid Id,
    string LanguageCode,
    string? Slug,
    string Title,
    string? Summary,
    string Markdown,
    PublicationStatusDto Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastEditedAt);
