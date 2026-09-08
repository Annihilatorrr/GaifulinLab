using GaifulinLab.Contracts.Articles;
using MediatR;

namespace GaifulinLab.Application.Articles.CreateArticle;

public sealed record CreateArticleCommand(
    string UserId,
    string LanguageCode,
    string? Title,
    string? Summary,
    string? Markdown,
    string? Slug,
    Guid? CoverMediaAssetId = null) : IRequest<CreateArticleResponse>;
