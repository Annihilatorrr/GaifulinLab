using GaifulinLab.Application.Content;
using Ganss.Xss;
using Markdig;

namespace GaifulinLab.Infrastructure.Content;

public sealed class MarkdownRenderer : IMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private readonly HtmlSanitizer _sanitizer = CreateSanitizer();

    public string Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var html = Markdown.ToHtml(markdown, Pipeline);
        return _sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(
        [
            "a", "blockquote", "br", "code", "del", "div", "em", "h1", "h2", "h3", "h4", "h5", "h6",
            "hr", "img", "input", "li", "ol", "p", "pre", "span", "strong", "sub", "sup", "table", "tbody",
            "td", "th", "thead", "tr", "ul"
        ]);

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(
        [
            "alt", "checked", "class", "disabled", "href", "src", "title", "type"
        ]);

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["http", "https", "mailto"]);
        sanitizer.AllowDataAttributes = false;
        sanitizer.KeepChildNodes = true;

        return sanitizer;
    }
}
