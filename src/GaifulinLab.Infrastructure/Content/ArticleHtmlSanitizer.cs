using AngleSharp.Dom;
using GaifulinLab.Application.Content;
using Ganss.Xss;

namespace GaifulinLab.Infrastructure.Content;

/// <summary>
/// Keeps article markup deliberately separate from the site's stylesheet and
/// executable surface. Articles may select documented <c>article-*</c> classes,
/// but never provide their own CSS or script.
/// </summary>
public sealed class ArticleHtmlSanitizer : IArticleHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer = CreateSanitizer();

    public string Sanitize(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return _sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(
        [
            "a", "article", "aside", "blockquote", "br", "code", "details", "div", "em", "figcaption",
            "figure", "footer", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "img", "kbd",
            "li", "mark", "ol", "p", "pre", "section", "small", "span", "strong", "sub", "summary",
            "sup", "table", "tbody", "td", "tfoot", "th", "thead", "tr", "ul"
        ]);

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(
        [
            "alt", "class", "colspan", "height", "href", "id", "loading", "open", "rowspan", "scope",
            "src", "title", "width"
        ]);

        // Do not let article markup opt into application-component classes.
        // The explicit language list covers the bundled highlighter's common use cases.
        sanitizer.AllowedClasses.Clear();
        sanitizer.AllowedClasses.UnionWith(
        [
            "article-callout", "article-callout--danger", "article-callout--deep",
            "article-callout--important", "article-callout--info", "article-callout--note",
            "article-callout--tip", "article-callout--warning", "article-example",
            "article-example__title", "article-figure", "article-figure--placeholder",
            "article-figure--center", "article-figure--left", "article-figure--right",
            "article-figure--small", "article-figure--wide", "article-figure__canvas",
            "article-figure__index", "article-clear",
            "article-formula", "article-formula--accent", "article-next", "article-next__eyebrow",
            "article-summary", "article-toc", "math",
            "language-bash", "language-c", "language-cpp", "language-csharp", "language-css",
            "language-go", "language-java", "language-javascript", "language-json", "language-php",
            "language-python", "language-rust", "language-sql", "language-typescript", "language-xml",
            "language-yaml"
        ]);

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["http", "https", "mailto"]);
        sanitizer.AllowDataAttributes = false;
        sanitizer.KeepChildNodes = true;

        // Add a system-only marker after class filtering, so authored markup cannot hide unrelated bold text.
        sanitizer.PostProcessDom += (_, args) =>
        {
            foreach (var strong in args.Document.QuerySelectorAll(
                "p > strong, aside.article-callout--important > strong, aside.article-callout--warning > strong"))
            {
                if (IsLeadingImportanceLabel(strong))
                {
                    strong.ClassList.Add("article-importance-label");
                }
            }
        };

        return sanitizer;
    }

    private static bool IsLeadingImportanceLabel(IElement strong)
    {
        if (strong.Children.Length != 0 || !IsImportanceLabel(strong.TextContent?.Trim()))
        {
            return false;
        }

        for (var previous = strong.PreviousSibling; previous is not null; previous = previous.PreviousSibling)
        {
            if (previous.NodeType != NodeType.Text || !string.IsNullOrWhiteSpace(previous.TextContent))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsImportanceLabel(string? text) =>
        string.Equals(text, "Важно:", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "Important:", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "Warning:", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "Warning", StringComparison.OrdinalIgnoreCase);
}
