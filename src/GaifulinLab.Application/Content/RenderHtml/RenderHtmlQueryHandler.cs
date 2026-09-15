using GaifulinLab.Application.Content;
using GaifulinLab.Contracts.Content;
using GaifulinLab.Domain.Common;
using MediatR;

namespace GaifulinLab.Application.Content.RenderHtml;

internal sealed class RenderHtmlQueryHandler(IArticleHtmlSanitizer sanitizer)
    : IRequestHandler<RenderHtmlQuery, HtmlPreviewResponse>
{
    public Task<HtmlPreviewResponse> Handle(
        RenderHtmlQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.Html);
        if (request.Html.Length > ContentLimits.ArticleHtml)
        {
            throw new ArgumentException(
                $"HTML cannot exceed {ContentLimits.ArticleHtml} characters.",
                nameof(request.Html));
        }

        return Task.FromResult(new HtmlPreviewResponse(sanitizer.RenderForDisplay(request.Html)));
    }
}
