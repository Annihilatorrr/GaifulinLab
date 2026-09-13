using GaifulinLab.Contracts.Content;
using MediatR;

namespace GaifulinLab.Application.Content.RenderHtml;

public sealed record RenderHtmlQuery(string Html) : IRequest<HtmlPreviewResponse>;
