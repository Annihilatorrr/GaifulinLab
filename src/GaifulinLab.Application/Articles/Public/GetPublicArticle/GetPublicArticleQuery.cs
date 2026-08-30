using GaifulinLab.Contracts.Articles;
using MediatR;

namespace GaifulinLab.Application.Articles.Public.GetPublicArticle;

public sealed record GetPublicArticleQuery(
    string LanguageCode,
    string Slug) : IRequest<PublicArticleDetailsDto>;
