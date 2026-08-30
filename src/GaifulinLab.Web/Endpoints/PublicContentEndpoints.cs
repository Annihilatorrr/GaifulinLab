using GaifulinLab.Application.Articles.Public.GetPublicArticle;
using GaifulinLab.Application.Articles.Public.GetPublicArticles;
using GaifulinLab.Application.Taxonomy.Public.GetPublicSeries;
using GaifulinLab.Application.Taxonomy.Public.GetPublicSeriesDetails;
using GaifulinLab.Application.Taxonomy.Public.GetPublicTags;
using GaifulinLab.Application.Taxonomy.Public.GetPublicTopics;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Web.Endpoints;

public static class PublicContentEndpoints
{
    public static IEndpointRouteBuilder MapPublicContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/public")
            .WithTags("Public Content")
            .AllowAnonymous()
            .AddEndpointFilter<ApiExceptionFilter>();

        group.MapGet("/articles", GetArticles)
            .Produces<IReadOnlyList<PublicArticleListItemDto>>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/articles/{languageCode}/{slug}", GetArticle)
            .Produces<PublicArticleDetailsDto>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/topics/{languageCode}", GetTopics)
            .Produces<IReadOnlyList<PublicTopicDto>>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/series/{languageCode}", GetSeries)
            .Produces<IReadOnlyList<PublicSeriesListItemDto>>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/series/{languageCode}/{slug}", GetSeriesDetails)
            .Produces<PublicSeriesDetailsDto>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/tags/{languageCode}", GetTags)
            .Produces<IReadOnlyList<PublicTagDto>>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private static async Task<IResult> GetArticles(
        string languageCode,
        string? topic,
        string? series,
        string? tag,
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new GetPublicArticlesQuery(languageCode, topic, series, tag),
            cancellationToken));

    private static async Task<IResult> GetArticle(
        string languageCode,
        string slug,
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new GetPublicArticleQuery(languageCode, slug),
            cancellationToken));

    private static async Task<IResult> GetTopics(
        string languageCode,
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetPublicTopicsQuery(languageCode), cancellationToken));

    private static async Task<IResult> GetSeries(
        string languageCode,
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetPublicSeriesQuery(languageCode), cancellationToken));

    private static async Task<IResult> GetSeriesDetails(
        string languageCode,
        string slug,
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new GetPublicSeriesDetailsQuery(languageCode, slug),
            cancellationToken));

    private static async Task<IResult> GetTags(
        string languageCode,
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetPublicTagsQuery(languageCode), cancellationToken));
}
