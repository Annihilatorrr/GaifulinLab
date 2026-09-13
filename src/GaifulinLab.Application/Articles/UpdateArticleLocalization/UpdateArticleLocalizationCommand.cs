using MediatR;

namespace GaifulinLab.Application.Articles.UpdateArticleLocalization;

public sealed record UpdateArticleLocalizationCommand(
    Guid ArticleId,
    string UserId,
    string LanguageCode,
    string? Title,
    string? Summary,
    string? Html,
    string? Slug,
    long? ExpectedVersion,
    Guid? CoverMediaAssetId = null) : IRequest<long>;
