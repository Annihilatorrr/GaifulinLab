using GaifulinLab.Contracts.Content;
using MediatR;

namespace GaifulinLab.Application.Content.RenderMarkdown;

internal sealed class RenderMarkdownQueryHandler(IMarkdownRenderer renderer)
    : IRequestHandler<RenderMarkdownQuery, MarkdownPreviewResponse>
{
    public Task<MarkdownPreviewResponse> Handle(
        RenderMarkdownQuery request,
        CancellationToken cancellationToken)
    {
        const int maximumMarkdownLength = 1_000_000;
        ArgumentNullException.ThrowIfNull(request.Markdown);

        if (request.Markdown.Length > maximumMarkdownLength)
        {
            throw new ArgumentException(
                $"Markdown cannot exceed {maximumMarkdownLength} characters.",
                nameof(request));
        }

        return Task.FromResult(new MarkdownPreviewResponse(renderer.Render(request.Markdown)));
    }
}
