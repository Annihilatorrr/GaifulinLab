using GaifulinLab.Application.Articles.CreateArticle;
using GaifulinLab.Application.Articles.DeleteArticle;
using GaifulinLab.Application.Articles.GetAdminArticle;
using GaifulinLab.Application.Articles.GetAdminArticles;
using GaifulinLab.Application.Articles.SetArticlePublication;
using GaifulinLab.Application.Articles.UpdateArticleLocalization;
using GaifulinLab.Application.Articles.UpdateArticleTaxonomy;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Infrastructure.Authentication;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin/articles")]
public sealed class AdminArticlesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminArticleListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminArticleListItemDto>>> GetArticles(
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAdminArticlesQuery(), cancellationToken));

    [HttpPost]
    [ProducesResponseType<CreateArticleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateArticleResponse>> CreateArticle(
        CreateArticleRequest request,
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

        return Created($"/api/admin/articles/{result.ArticleId}", result);
    }

    [HttpGet("{articleId:guid}")]
    [ProducesResponseType<AdminArticleDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminArticleDetailsDto>> GetArticle(
        Guid articleId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAdminArticleQuery(articleId), cancellationToken));

    [HttpPut("{articleId:guid}/localizations/{languageCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertLocalization(
        Guid articleId,
        string languageCode,
        UpdateArticleLocalizationRequest request,
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

        return NoContent();
    }

    [HttpPost("{articleId:guid}/localizations/{languageCode}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Publish(
        Guid articleId,
        string languageCode,
        CancellationToken cancellationToken) =>
        SetPublication(articleId, languageCode, true, cancellationToken);

    [HttpPost("{articleId:guid}/localizations/{languageCode}/unpublish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Unpublish(
        Guid articleId,
        string languageCode,
        CancellationToken cancellationToken) =>
        SetPublication(articleId, languageCode, false, cancellationToken);

    [HttpPut("{articleId:guid}/taxonomy")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTaxonomy(
        Guid articleId,
        UpdateArticleTaxonomyRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new UpdateArticleTaxonomyCommand(
                articleId,
                request.TopicIds,
                request.Series,
                request.Tags),
            cancellationToken);

        return NoContent();
    }

    [HttpDelete("{articleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteArticle(Guid articleId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteArticleCommand(articleId), cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult> SetPublication(
        Guid articleId,
        string languageCode,
        bool publish,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new SetArticlePublicationCommand(articleId, languageCode, publish),
            cancellationToken);

        return NoContent();
    }
}
