using GaifulinLab.Contracts.Articles;
using GaifulinLab.Domain.Articles;

namespace GaifulinLab.Application.Articles;

internal static class AdminArticleMappings
{
    public static AdminArticleLocalizationDto ToDetails(this ArticleLocalization localization) => new(
        localization.Id,
        localization.Version,
        localization.LanguageCode,
        localization.Slug,
        localization.Title,
        localization.Summary,
        localization.Markdown,
        (PublicationStatusDto)localization.Status,
        localization.PublishedAt,
        localization.UpdatedAt,
        localization.LastEditedAt);

    public static AdminArticleLocalizationSummaryDto ToSummary(this ArticleLocalization localization) => new(
        localization.Id,
        localization.LanguageCode,
        localization.Slug,
        localization.Title,
        (PublicationStatusDto)localization.Status,
        localization.PublishedAt,
        localization.UpdatedAt,
        localization.LastEditedAt);
}
