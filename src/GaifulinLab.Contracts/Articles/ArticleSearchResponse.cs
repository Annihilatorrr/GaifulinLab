namespace GaifulinLab.Contracts.Articles;

public sealed record ArticleSearchResponse(
    IReadOnlyList<PublicArticleListItemDto> Items, long TotalCount, int Page, int PageSize, int TotalPages);
