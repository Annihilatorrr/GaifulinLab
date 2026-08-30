namespace GaifulinLab.Contracts.Articles;

public sealed record AdminArticleListItemDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AdminArticleLocalizationSummaryDto> Localizations);
