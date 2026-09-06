using GaifulinLab.Application.Taxonomy.GetAdminTaxonomy;
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

    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated user does not have an identifier.");
}
