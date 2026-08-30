using GaifulinLab.Application.Articles.CreateArticle;
using GaifulinLab.Application.Articles.DeleteArticle;
using GaifulinLab.Application.Articles.GetAdminArticle;
using GaifulinLab.Application.Articles.GetAdminArticles;
using GaifulinLab.Application.Articles.SetArticlePublication;
using GaifulinLab.Application.Articles.UpdateArticleLocalization;
using GaifulinLab.Application.Articles.UpdateArticleTaxonomy;
using GaifulinLab.Application.Taxonomy.GetAdminTaxonomy;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Infrastructure.Authentication;
using MediatR;

namespace GaifulinLab.Web.Endpoints;

public static class AdminArticleEndpoints
{
    public static IEndpointRouteBuilder MapAdminArticleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .AddEndpointFilter<ApiExceptionFilter>();

        group.MapGet("/articles", GetArticles)
            .Produces<IReadOnlyList<AdminArticleListItemDto>>();

        group.MapPost("/articles", CreateArticle)
            .Produces<CreateArticleResponse>(StatusCodes.Status201Created)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

        group.MapGet("/articles/{articleId:guid}", GetArticle)
            .Produces<AdminArticleDetailsDto>()
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPut("/articles/{articleId:guid}/localizations/{languageCode}", UpsertLocalization)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/articles/{articleId:guid}/localizations/{languageCode}/publish", Publish)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/articles/{articleId:guid}/localizations/{languageCode}/unpublish", Unpublish)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPut("/articles/{articleId:guid}/taxonomy", UpdateTaxonomy)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/articles/{articleId:guid}", DeleteArticle)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/taxonomy", GetTaxonomy)
            .Produces<AdminTaxonomyDto>();

        return endpoints;
    }

    private static async Task<IResult> GetArticles(ISender sender, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetAdminArticlesQuery(), cancellationToken));

    private static async Task<IResult> CreateArticle(
        CreateArticleRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateArticleCommand(
                request.LanguageCode,
                request.Title,
                request.Summary,
                request.Markdown,
                request.Slug),
            cancellationToken);

        return Results.Created($"/api/admin/articles/{result.ArticleId}", result);
    }

    private static async Task<IResult> GetArticle(
        Guid articleId,
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetAdminArticleQuery(articleId), cancellationToken));

    private static async Task<IResult> UpsertLocalization(
        Guid articleId,
        string languageCode,
        UpdateArticleLocalizationRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new UpdateArticleLocalizationCommand(
                articleId,
                languageCode,
                request.Title,
                request.Summary,
                request.Markdown,
                request.Slug),
            cancellationToken);
        return Results.NoContent();
    }

    private static Task<IResult> Publish(
        Guid articleId,
        string languageCode,
        ISender sender,
        CancellationToken cancellationToken) =>
        SetPublication(articleId, languageCode, true, sender, cancellationToken);

    private static Task<IResult> Unpublish(
        Guid articleId,
        string languageCode,
        ISender sender,
        CancellationToken cancellationToken) =>
        SetPublication(articleId, languageCode, false, sender, cancellationToken);

    private static async Task<IResult> SetPublication(
        Guid articleId,
        string languageCode,
        bool publish,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new SetArticlePublicationCommand(articleId, languageCode, publish),
            cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateTaxonomy(
        Guid articleId,
        UpdateArticleTaxonomyRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new UpdateArticleTaxonomyCommand(
                articleId,
                request.TopicIds,
                request.Series,
                request.Tags),
            cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteArticle(
        Guid articleId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteArticleCommand(articleId), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetTaxonomy(
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetAdminTaxonomyQuery(), cancellationToken));
}
