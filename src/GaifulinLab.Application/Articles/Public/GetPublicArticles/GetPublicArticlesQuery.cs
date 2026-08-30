using GaifulinLab.Contracts.Articles;
using MediatR;

namespace GaifulinLab.Application.Articles.Public.GetPublicArticles;

public sealed record GetPublicArticlesQuery(
    string LanguageCode,
    string? TopicSlug,
    string? SeriesSlug,
    string? Tag) : IRequest<IReadOnlyList<PublicArticleListItemDto>>;
