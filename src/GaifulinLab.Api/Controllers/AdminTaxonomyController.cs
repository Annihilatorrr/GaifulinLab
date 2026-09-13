using GaifulinLab.Application.Taxonomy.AddSeriesLocalization;
using GaifulinLab.Application.Taxonomy.AddTopicLocalization;
using GaifulinLab.Application.Taxonomy.CreateSeries;
using GaifulinLab.Application.Taxonomy.CreateTopic;
using GaifulinLab.Application.Taxonomy.GetAdminTaxonomy;
using GaifulinLab.Application.Taxonomy.UpdateSeriesLocalization;
using GaifulinLab.Application.Taxonomy.UpdateTopicLocalization;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Infrastructure.Authentication;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/taxonomy")]
public sealed class AdminTaxonomyController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AdminTaxonomyDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminTaxonomyDto>> GetTaxonomy(CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new GetAdminTaxonomyQuery(GetCurrentUserId()),
            cancellationToken));

    [HttpPost("topics")]
    [ProducesResponseType<AdminTopicDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminTopicDto>> CreateTopic(
        CreateTopicRequest request,
        CancellationToken cancellationToken)
    {
        var created = await sender.Send(
            new CreateTopicCommand(request.LanguageCode, request.Name, request.Slug, request.Description),
            cancellationToken);
        return Created("/api/admin/taxonomy", created);
    }

    [HttpPost("topics/{topicId:guid}/localizations")]
    [ProducesResponseType<AdminTopicDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminTopicDto>> AddTopicLocalization(
        Guid topicId,
        CreateTopicRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await sender.Send(
            new AddTopicLocalizationCommand(
                topicId,
                request.LanguageCode,
                request.Name,
                request.Slug,
                request.Description),
            cancellationToken);
        return Created("/api/admin/taxonomy", updated);
    }

    [HttpPut("topics/{topicId:guid}/localizations/{languageCode}")]
    [ProducesResponseType<AdminTopicDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminTopicDto>> UpdateTopicLocalization(
        Guid topicId,
        string languageCode,
        UpdateTopicLocalizationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateTopicLocalizationCommand(
                topicId,
                languageCode,
                request.Name,
                request.Slug,
                request.Description),
            cancellationToken));

    [HttpPost("series")]
    [ProducesResponseType<AdminSeriesDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminSeriesDto>> CreateSeries(
        CreateSeriesRequest request,
        CancellationToken cancellationToken)
    {
        var created = await sender.Send(
            new CreateSeriesCommand(request.LanguageCode, request.Title, request.Slug, request.Description),
            cancellationToken);
        return Created("/api/admin/taxonomy", created);
    }

    [HttpPost("series/{seriesId:guid}/localizations")]
    [ProducesResponseType<AdminSeriesDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminSeriesDto>> AddSeriesLocalization(
        Guid seriesId,
        CreateSeriesRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await sender.Send(
            new AddSeriesLocalizationCommand(
                seriesId,
                request.LanguageCode,
                request.Title,
                request.Slug,
                request.Description),
            cancellationToken);
        return Created("/api/admin/taxonomy", updated);
    }

    [HttpPut("series/{seriesId:guid}/localizations/{languageCode}")]
    [ProducesResponseType<AdminSeriesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminSeriesDto>> UpdateSeriesLocalization(
        Guid seriesId,
        string languageCode,
        UpdateSeriesLocalizationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateSeriesLocalizationCommand(
                seriesId,
                languageCode,
                request.Title,
                request.Slug,
                request.Description),
            cancellationToken));

    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated user does not have an identifier.");
}
