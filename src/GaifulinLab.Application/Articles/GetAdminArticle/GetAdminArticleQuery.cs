using GaifulinLab.Contracts.Articles;
using MediatR;

namespace GaifulinLab.Application.Articles.GetAdminArticle;

public sealed record GetAdminArticleQuery(Guid ArticleId) : IRequest<AdminArticleDetailsDto>;
