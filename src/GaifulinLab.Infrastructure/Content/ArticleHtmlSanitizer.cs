using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using GaifulinLab.Application.Content;
using Ganss.Xss;
using System.Text.RegularExpressions;

namespace GaifulinLab.Infrastructure.Content;

/// <summary>
/// Keeps article markup deliberately separate from the site's stylesheet and
/// executable surface. Articles may select documented <c>article-*</c> classes,
/// but never provide their own CSS or script.
/// </summary>
public sealed partial class ArticleHtmlSanitizer : IArticleHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer = CreateSanitizer();
    private readonly ArticleSvgSanitizer _svgSanitizer = new();

    public string Sanitize(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var fragments = new List<(string Placeholder, string Source)>();
        var markupWithoutSvg = SvgDocumentRegex().Replace(html, match =>
        {
            var placeholder = $"article-svg-placeholder-{Guid.NewGuid():N}";
            fragments.Add((placeholder, match.Groups["svg"].Value));
            return placeholder;
        });

        var sanitized = _sanitizer.Sanitize(markupWithoutSvg);
        var occupiedIds = new HtmlParser()
            .ParseDocument(sanitized)
            .QuerySelectorAll("[id]")
            .Select(element => element.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (placeholder, source, ordinal) in fragments.Select((fragment, ordinal) => (fragment.Placeholder, fragment.Source, ordinal)))
        {
            var svg = _svgSanitizer.Sanitize(source, ordinal, occupiedIds);
            if (svg is null)
            {
                sanitized = sanitized.Replace(placeholder, string.Empty, StringComparison.Ordinal);
                continue;
            }

            foreach (var id in new HtmlParser().ParseDocument(svg).QuerySelectorAll("[id]").Select(element => element.Id).Where(id => id is not null))
            {
                occupiedIds.Add(id!);
            }

            sanitized = sanitized.Replace(placeholder, svg, StringComparison.Ordinal);
        }

        return sanitized;
    }

    public string RenderForDisplay(string html)
    {
        var sanitized = Sanitize(html);
        var document = new HtmlParser().ParseDocument(sanitized);
        RebuildTableOfContents(document);
        return document.Body?.InnerHtml ?? string.Empty;
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
            "article-formula", "article-formula--accent", "article-formula--large", "article-next", "article-next__eyebrow",
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
            NormalizeTableOfContents(args.Document);

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

    private static void NormalizeTableOfContents(IDocument document)
    {
        var tableOfContents = document.QuerySelectorAll(".article-toc");
        if (tableOfContents.Length == 0)
        {
            return;
        }

        foreach (var tableOfContentsElement in tableOfContents)
        {
            var title = tableOfContentsElement.Children.FirstOrDefault(
                element => string.Equals(element.LocalName, "h2", StringComparison.OrdinalIgnoreCase));
            var titleText = title?.TextContent;

            foreach (var child in tableOfContentsElement.ChildNodes.ToArray())
            {
                tableOfContentsElement.RemoveChild(child);
            }

            if (titleText is not null)
            {
                var normalizedTitle = document.CreateElement("h2");
                normalizedTitle.TextContent = titleText;
                tableOfContentsElement.AppendChild(normalizedTitle);
            }
        }
    }

    private static void RebuildTableOfContents(IDocument document)
    {
        var tableOfContents = document.QuerySelectorAll(".article-toc");
        if (tableOfContents.Length == 0)
        {
            return;
        }

        var idCounts = document.QuerySelectorAll("[id]")
            .Select(element => element.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var occupiedIds = new HashSet<string>(idCounts.Keys, StringComparer.Ordinal);
        var generatedIdNumber = 1;
        var headings = GetTableOfContentsHeadings(document);

        // An id must identify exactly one section and be safe to interpolate into a raw fragment link.
        foreach (var heading in headings)
        {
            var section = heading.ParentElement!;
            var id = section.Id;
            if (IsValidArticleFragmentId(id)
                && (!idCounts.TryGetValue(id!, out var count) || count == 1))
            {
                continue;
            }

            string generatedId;
            do
            {
                generatedId = $"article-section-{generatedIdNumber++}";
            }
            while (!occupiedIds.Add(generatedId));

            section.Id = generatedId;
        }

        foreach (var tableOfContentsElement in tableOfContents)
        {
            var lists = tableOfContentsElement.Children
                .Where(element => string.Equals(element.LocalName, "ol", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var list = lists.FirstOrDefault() ?? document.CreateElement("ol");

            foreach (var extraList in lists.Skip(1))
            {
                extraList.Remove();
            }

            foreach (var child in list.ChildNodes.ToArray())
            {
                list.RemoveChild(child);
            }
            if (lists.Length == 0)
            {
                tableOfContentsElement.AppendChild(list);
            }

            foreach (var heading in headings)
            {
                var item = document.CreateElement("li");
                var link = document.CreateElement("a");
                link.SetAttribute("href", $"#{heading.ParentElement!.Id}");
                link.TextContent = heading.TextContent.Trim();
                item.AppendChild(link);
                list.AppendChild(item);
            }
        }
    }

    private static IElement[] GetTableOfContentsHeadings(IDocument document) =>
        document.QuerySelectorAll("section > h2")
            .Where(heading => !IsInsideExcludedArticleContent(heading))
            .ToArray();

    private static bool IsInsideExcludedArticleContent(IElement element)
    {
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor.ClassList.Contains("article-toc")
                || ancestor.ClassList.Contains("article-next")
                || ancestor.ClassList.Contains("article-callout")
                || ancestor.ClassList.Contains("article-example")
                || ancestor.ClassList.Contains("article-summary"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidArticleFragmentId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && !id.Any(character => character is ' ' or '\t' or '\n' or '\r' or '\f');

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

    // The optional XML declaration and SVG doctype are consumed with the SVG itself,
    // before XML parsing. The SVG parser never receives a DTD and cannot resolve one.
    [GeneratedRegex("(?:<\\?xml\\b[\\s\\S]*?\\?>\\s*)?(?:<!DOCTYPE\\s+svg\\b[\\s\\S]*?>\\s*)?(?<svg><svg\\b[\\s\\S]*?</svg\\s*>)", RegexOptions.IgnoreCase)]
    private static partial Regex SvgDocumentRegex();
}
