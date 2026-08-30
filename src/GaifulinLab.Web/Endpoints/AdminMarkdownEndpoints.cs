using GaifulinLab.Application.Content.RenderMarkdown;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Content;
using GaifulinLab.Infrastructure.Authentication;
using MediatR;

namespace GaifulinLab.Web.Endpoints;

public static class AdminMarkdownEndpoints
{
    public static IEndpointRouteBuilder MapAdminMarkdownEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/markdown")
            .WithTags("Admin Markdown")
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .AddEndpointFilter<AdminApiExceptionFilter>();

        group.MapPost("/preview", Preview)
            .Produces<MarkdownPreviewResponse>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private static async Task<IResult> Preview(
        MarkdownPreviewRequest request,
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new RenderMarkdownQuery(request.Markdown), cancellationToken));
}
