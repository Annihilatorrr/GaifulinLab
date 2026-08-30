using MediatR;

namespace GaifulinLab.Application.Articles.SetArticlePublication;

public sealed record SetArticlePublicationCommand(
    Guid ArticleId,
    string LanguageCode,
    bool Publish) : IRequest;
