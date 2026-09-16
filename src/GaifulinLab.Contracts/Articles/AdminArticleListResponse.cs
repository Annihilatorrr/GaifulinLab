namespace GaifulinLab.Contracts.Articles;

public sealed record AdminArticleListResponse(
    IReadOnlyList<AdminArticleListItemDto> Items,
    long TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
