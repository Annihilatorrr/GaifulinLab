using GaifulinLab.Contracts.Content;
using MediatR;

namespace GaifulinLab.Application.Content.RenderMarkdown;

public sealed record RenderMarkdownQuery(string Markdown) : IRequest<MarkdownPreviewResponse>;
