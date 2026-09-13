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
            "article-callout", "article-callout--danger", "article-callout--info",
            "article-callout--warning", "article-figure", "article-formula", "math",
            "language-bash", "language-c", "language-cpp", "language-csharp", "language-css",
            "language-go", "language-java", "language-javascript", "language-json", "language-php",
            "language-python", "language-rust", "language-sql", "language-typescript", "language-xml",
            "language-yaml"
        ]);

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["http", "https", "mailto"]);
        sanitizer.AllowDataAttributes = false;
        sanitizer.KeepChildNodes = true;

        return sanitizer;
    }
}
