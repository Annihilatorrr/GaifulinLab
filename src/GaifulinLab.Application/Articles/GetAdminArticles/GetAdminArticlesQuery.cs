using GaifulinLab.Contracts.Articles;
using MediatR;

namespace GaifulinLab.Application.Articles.GetAdminArticles;

public sealed record GetAdminArticlesQuery(
    string UserId,
    int Page,
    int PageSize) : IRequest<AdminArticleListResponse>;
