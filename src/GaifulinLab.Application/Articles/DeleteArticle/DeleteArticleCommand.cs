using MediatR;

namespace GaifulinLab.Application.Articles.DeleteArticle;

public sealed record DeleteArticleCommand(Guid ArticleId) : IRequest;
