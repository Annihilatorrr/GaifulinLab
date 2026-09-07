using GaifulinLab.Domain.Articles;
using MediatR;

namespace GaifulinLab.Application.Articles.SetArticlePublication;

public sealed record SetArticlePublicationCommand(
    Guid ArticleId,
    string UserId,
    string LanguageCode,
    PublicationStatus TargetStatus) : IRequest;
