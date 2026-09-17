using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using GaifulinLab.Infrastructure.Content;
using System.Text;
using System.Xml.Linq;

namespace GaifulinLab.Infrastructure.Tests.Content;

public sealed class ArticleHtmlSanitizerTests
{
    private readonly ArticleHtmlSanitizer _sanitizer = new();

    [Fact]
    public void Sanitize_PreservesTheDocumentedArticleMarkup()
    {
        const string html = """
            <section>
                <h2>Heading</h2>
                <aside class="article-callout article-callout--warning"><strong>Careful</strong><p>Text</p></aside>
                <figure><img src="/media/00000000-0000-0000-0000-000000000001" alt="Diagram"><figcaption>Caption</figcaption></figure>
                <pre><code class="language-csharp">return 1;</code></pre>
            </section>
            """;

        var sanitized = _sanitizer.Sanitize(html);

        Assert.Contains("<section>", sanitized);
        Assert.Contains("article-callout--warning", sanitized);
        Assert.Contains("language-csharp", sanitized);
        Assert.Contains("<figcaption>Caption</figcaption>", sanitized);
    }

    [Fact]
    public void Sanitize_RemovesExecutableMarkupInlineStylesAndUnsafeUris()
    {
        const string html = """
            <script>alert('xss')</script>
            <img src="/media/image.png" onerror="alert('xss')" style="display:none">
            <a href="javascript:alert('xss')">unsafe link</a>
            <iframe src="https://example.com"></iframe>
            <p>Safe text</p>
            """;

        var sanitized = _sanitizer.Sanitize(html);

        Assert.Contains("Safe text", sanitized);
        Assert.DoesNotContain("<script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_PreservesTheMatplotlibInlineSvgProfileAndNormalizesItsUnsafeContainerMarkup()
    {
        const string html = """
            <?xml version="1.0" encoding="utf-8" standalone="no"?>
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="792pt" height="540pt" viewBox="0 0 792 540">
              <metadata><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" /></metadata>
              <defs><style type="text/css">*{stroke-linejoin: round; stroke-linecap: butt}</style><path id="marker" d="M 0 0 L 0 3.5" style="stroke: #a6b4cb; stroke-width: 0.8" /></defs>
              <g id="axes"><path id="line" d="M 0 0 L 20 20" clip-path="url(#clip)" style="fill: none; stroke: #303c52; stroke-width: 0.7" /><use xlink:href="#marker" x="10" y="12" style="fill: #a6b4cb" /><text x="10" y="20" style="font-size: 11px; font-family: 'DejaVu Sans'; text-anchor: middle; fill: #a6b4cb">График</text></g>
              <defs><clipPath id="clip"><rect x="0" y="0" width="20" height="20" /></clipPath></defs>
            </svg>
            """;

        var sanitized = _sanitizer.Sanitize(html);
        var document = Parse(sanitized);
        var svg = Assert.Single(document.QuerySelectorAll("svg"));
        var marker = Assert.Single(svg.QuerySelectorAll("path[id$='marker']"));
        var use = Assert.Single(svg.QuerySelectorAll("use"));
        var line = Assert.Single(svg.QuerySelectorAll("path[id$='line']"));
        var clipPath = Assert.Single(svg.QuerySelectorAll("clipPath"));

        Assert.Equal("0 0 792 540", svg.GetAttribute("viewBox"));
        Assert.Equal($"#{marker.Id}", use.GetAttribute("href"));
        Assert.Equal($"url(#{clipPath.Id})", line.GetAttribute("clip-path"));
        Assert.Equal("#303c52", line.GetAttribute("stroke"));
        Assert.Equal("round", line.GetAttribute("stroke-linejoin"));
        Assert.Equal("butt", line.GetAttribute("stroke-linecap"));
        Assert.Equal("График", svg.QuerySelector("text")!.TextContent);
        Assert.Empty(svg.QuerySelectorAll("metadata, style, [style], [xlink\\:href]"));
        Assert.DoesNotContain("DOCTYPE", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rdf:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sanitized, _sanitizer.Sanitize(sanitized));
    }

    [Fact]
    public void Sanitize_PreservesTheExactSuppliedMatplotlibSvgAsAGoldenFixture()
    {
        var source = MatplotlibSvgFixture.Get();
        var sanitized = _sanitizer.Sanitize(source);
        var document = Parse(sanitized);
        var svg = Assert.Single(document.QuerySelectorAll("svg"));
        var paths = svg.QuerySelectorAll("path");
        var uses = svg.QuerySelectorAll("use");
        var clipPath = Assert.Single(svg.QuerySelectorAll("clipPath"));
        var tspans = svg.QuerySelectorAll("tspan");

        Assert.Equal(31_493, Encoding.UTF8.GetByteCount(source));
        Assert.True(paths.Length > 30);
        Assert.True(uses.Length > 10);
        Assert.True(tspans.Length > 20);
        Assert.Equal("0 0 792 540", svg.GetAttribute("viewBox"));
        Assert.Contains("Поворот", svg.TextContent);
        Assert.All(svg.QuerySelectorAll("[id]"), element => Assert.StartsWith("article-svg-", element.Id));
        Assert.All(uses, use => Assert.NotNull(svg.QuerySelector(use.GetAttribute("href")!)));
        Assert.All(svg.QuerySelectorAll("[clip-path]"), element => Assert.NotNull(svg.QuerySelector(element.GetAttribute("clip-path")![4..^1])));
        Assert.NotNull(clipPath.QuerySelector("rect"));
        Assert.Contains(paths, path => path.GetAttribute("stroke-dasharray") == "4.4,5.5");
        Assert.Contains(tspans, tspan => tspan.GetAttribute("font-style") == "oblique");
        Assert.Empty(svg.QuerySelectorAll("metadata, style, [style], script, foreignObject, image"));
        Assert.DoesNotContain("DOCTYPE", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sanitized, _sanitizer.Sanitize(sanitized));
    }

    [Fact]
    public void Sanitize_IsolatesSvgIdsFromArticleMarkupAndOtherSvgFragments()
    {
        const string html = """
            <p id="article-svg-1--marker">Article target</p>
            <svg viewBox="0 0 1 1"><defs><path id="article-svg-1--marker" d="M 0 0" /></defs><use href="#article-svg-1--marker" /></svg>
            <svg viewBox="0 0 1 1"><defs><path id="article-svg-1--marker" d="M 0 0" /></defs><use href="#article-svg-1--marker" /></svg>
            """;

        var sanitized = _sanitizer.Sanitize(html);
        var document = Parse(sanitized);
        var svgs = document.QuerySelectorAll("svg");
        var articleId = document.QuerySelector("p")!.Id;
        var firstMarker = svgs[0].QuerySelector("path")!.Id;
        var secondMarker = svgs[1].QuerySelector("path")!.Id;

        Assert.Equal("article-svg-1--marker", articleId);
        Assert.NotEqual(articleId, firstMarker);
        Assert.NotEqual(firstMarker, secondMarker);
        Assert.Equal($"#{firstMarker}", svgs[0].QuerySelector("use")!.GetAttribute("href"));
        Assert.Equal($"#{secondMarker}", svgs[1].QuerySelector("use")!.GetAttribute("href"));
        Assert.Equal(sanitized, _sanitizer.Sanitize(sanitized));
    }

    [Fact]
    public void Sanitize_RemovesExecutableAndExternalSvgContentWithoutRemovingSafeShapes()
    {
        const string html = """
            <svg viewBox="0 0 20 20" onload="alert('xss')">
              <path d="M 0 0 L 20 20" style="stroke: #000" />
              <script>alert('xss')</script><foreignObject><iframe src="https://example.test"></iframe></foreignObject>
              <image href="https://example.test/image.svg" /><use href="javascript:alert('xss')" />
            </svg>
            """;

        var sanitized = _sanitizer.Sanitize(html);

        Assert.Contains("<svg", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<path", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foreignObject", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<image", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://example.test", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_RemovesApplicationClassesButPreservesDocumentedArticleClasses()
    {
        var sanitized = _sanitizer.Sanitize(
            "<aside class=\"article-callout button\">Note</aside><code class=\"language-python menu\">pass</code>");

        Assert.Contains("article-callout", sanitized);
        Assert.Contains("language-python", sanitized);
        Assert.DoesNotContain("button", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("menu", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_PreservesArticleLayoutClasses()
    {
        const string html = """
            <aside class="article-summary article-toc article-example article-example__title
                article-callout--note article-callout--important article-callout--tip article-callout--deep
                article-formula--accent article-formula--large article-figure--placeholder article-figure--center article-figure--left
                article-figure--right article-figure--small article-figure--wide article-figure__canvas
                article-figure__index article-clear article-next article-next__eyebrow">Content</aside>
            """;
        string[] expectedClasses =
        [
            "article-summary", "article-toc", "article-example", "article-example__title",
            "article-callout--note", "article-callout--important", "article-callout--tip",
            "article-callout--deep", "article-formula--accent", "article-formula--large", "article-figure--placeholder",
            "article-figure--center", "article-figure--left", "article-figure--right",
            "article-figure--small", "article-figure--wide", "article-figure__canvas",
            "article-figure__index", "article-clear", "article-next", "article-next__eyebrow"
        ];

        var sanitized = _sanitizer.Sanitize(html);
        var actualClasses = XElement
            .Parse(sanitized)
            .Attribute("class")!
            .Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(expectedClasses.Order(), actualClasses.Order());
    }

    [Fact]
    public void Sanitize_PreservesLatexTextForClientTypesetting()
    {
        var sanitized = _sanitizer.Sanitize("<p>Inline \\(X\\) and display \\[Y\\]</p>");

        Assert.Contains("\\(X\\)", sanitized);
        Assert.Contains("\\[Y\\]", sanitized);
    }

    [Fact]
    public void Sanitize_MarksLeadingImportanceLabelsInParagraphsAndCallouts()
    {
        const string html = """
            <p> <strong>Важно:</strong> Прочитайте текст.</p>
            <p><strong>Important:</strong> Read the text.</p>
            <aside class="article-callout article-callout--warning"><strong>Warning</strong><p>Read this.</p></aside>
            <aside class="article-callout article-callout--important"><strong>Важно:</strong><p>Прочитайте это.</p></aside>
            """;

        // Sanitization marks only the known leading labels, leaving their text in the document.
        var sanitized = _sanitizer.Sanitize(html);
        var document = new HtmlParser().ParseDocument(sanitized);
        var labels = document.QuerySelectorAll("strong.article-importance-label");
        Assert.Equal(4, labels.Length);
        Assert.Equal(["Важно:", "Important:", "Warning", "Важно:"], labels.Select(label => label.TextContent.Trim()));

        // Repeated sanitization still produces the same system marker after authored classes are filtered.
        var repeated = new HtmlParser().ParseDocument(_sanitizer.Sanitize(sanitized));
        Assert.Equal(4, repeated.QuerySelectorAll("strong.article-importance-label").Length);
    }

    [Fact]
    public void Sanitize_DoesNotMarkUnrelatedBoldTextOrForgedMarkers()
    {
        const string html = """
            <p>Read <strong>Важно:</strong> later.</p>
            <p><strong>Важная причина:</strong> Details.</p>
            <p><strong class="article-importance-label">Other text</strong> Details.</p>
            <aside class="article-callout article-callout--warning"><strong>Careful</strong><p>Details.</p></aside>
            """;

        // These bold spans convey article content, so none may acquire the visual replacement marker.
        var sanitized = _sanitizer.Sanitize(html);
        var document = new HtmlParser().ParseDocument(sanitized);
        Assert.Empty(document.QuerySelectorAll(".article-importance-label"));
        Assert.Equal(4, document.QuerySelectorAll("strong").Length);
        Assert.Contains("Careful", sanitized);
    }

    [Fact]
    public void Sanitize_NormalizesTableOfContentsToAPlaceholderAndPreservesSectionIds()
    {
        const string html = """
            <div class="article-toc"><h2 id="toc-title"><strong>Contents</strong></h2><ol><li><a href="#outdated">Outdated</a></li></ol><p>Stale content</p></div>
            <p id="article-section-1">An authored non-section target</p>
            <section id="article-section-2"><h2>First topic</h2><p>Text</p></section>
            <section id="chapter one"><h2>Second topic</h2><p>Text</p></section>
            """;

        var document = Parse(_sanitizer.Sanitize(html));
        var tableOfContents = Assert.Single(document.QuerySelectorAll(".article-toc"));
        var sections = document.QuerySelectorAll("section");

        var title = Assert.Single(tableOfContents.Children, element => element.LocalName == "h2");
        Assert.Equal("Contents", title.TextContent);
        Assert.Empty(title.Attributes);
        Assert.Empty(tableOfContents.QuerySelectorAll("ol"));
        Assert.DoesNotContain("Stale content", tableOfContents.TextContent);
        Assert.Equal("article-section-1", document.QuerySelector("p")!.Id);
        Assert.Equal("article-section-2", sections[0].Id);
        Assert.Equal("chapter one", sections[1].Id);
    }

    [Fact]
    public void RenderForDisplay_BuildsTableOfContentsAndGeneratesTransientSectionTargets()
    {
        const string html = """
            <div class="article-toc"><h2>Contents</h2></div>
            <section><h2>First topic</h2><p>Text</p></section>
            <section><h2>Second topic</h2><p>Text</p></section>
            """;

        var source = _sanitizer.Sanitize(html);
        var document = Parse(_sanitizer.RenderForDisplay(html));
        var sections = document.QuerySelectorAll("section");
        var links = GetTableOfContentsLinks(document);

        Assert.DoesNotContain("<ol", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("article-section-", source, StringComparison.Ordinal);
        Assert.Equal(["article-section-1", "article-section-2"], sections.Select(section => section.Id));
        Assert.Equal(["#article-section-1", "#article-section-2"], links.Select(link => link.GetAttribute("href")));
        Assert.Equal(["First topic", "Second topic"], links.Select(link => link.TextContent.Trim()));
    }

    [Fact]
    public void RenderForDisplay_RebuildsStaleEntriesAndExcludesUtilityHeadings()
    {
        const string html = """
            <div class="article-toc">
                <h2 id="toc-title">Contents</h2>
                <section><h2>Must not appear</h2></section>
                <ol><li><a href="#old">Old title</a></li><li><a href="#also-old">Also old</a></li></ol>
            </div>
            <section class="article-next"><h2>Next topic</h2></section>
            <section><h2>Renamed topic</h2></section>
            """;

        var document = Parse(_sanitizer.RenderForDisplay(html));
        var links = GetTableOfContentsLinks(document);

        // Only article-section headings participate; ToC and next-topic content never reference themselves.
        var link = Assert.Single(links);
        Assert.Equal("Renamed topic", link.TextContent.Trim());
        Assert.DoesNotContain("Old title", document.Body!.TextContent);
        Assert.DoesNotContain("Must not appear", links.Select(item => item.TextContent));
        Assert.DoesNotContain("Next topic", links.Select(item => item.TextContent));
        Assert.DoesNotContain("#old", links.Select(item => item.GetAttribute("href")));
    }

    [Fact]
    public void RenderForDisplay_PreservesUniqueAuthoredTargetsAndGeneratesCollisionSafeTargets()
    {
        const string html = """
            <div class="article-toc"><h2>Contents</h2></div>
            <p id="article-section-1">Existing target</p>
            <section id="chapter-one"><h2>First</h2></section>
            <section id="duplicate"><h2>Second</h2></section>
            <section id="duplicate"><h2>Third</h2></section>
            <section id=" "><h2>Fourth</h2></section>
            <section><h2>Fifth</h2></section>
            """;

        var document = Parse(_sanitizer.RenderForDisplay(html));
        var sections = document.QuerySelectorAll("section");
        var sectionIds = sections.Select(section => section.Id).ToArray();
        var links = GetTableOfContentsLinks(document);

        Assert.Equal("chapter-one", sectionIds[0]);
        Assert.Equal(sectionIds.Length, sectionIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(sectionIds, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.All(links, link => Assert.Contains(link.GetAttribute("href")![1..], sectionIds));
        Assert.DoesNotContain("#duplicate", links.Select(link => link.GetAttribute("href")));
        Assert.DoesNotContain("#article-section-1", links.Select(link => link.GetAttribute("href")));
    }

    [Fact]
    public void RenderForDisplay_IsIdempotent()
    {
        const string html = """
            <div class="article-toc"><h2>Contents</h2></div>
            <section><h2>Topic</h2></section>
            """;

        var rendered = _sanitizer.RenderForDisplay(html);

        Assert.Equal(rendered, _sanitizer.RenderForDisplay(rendered));
    }

    private static IDocument Parse(string html) =>
        new HtmlParser().ParseDocument(html);

    private static IElement[] GetTableOfContentsLinks(IDocument document)
    {
        var tableOfContents = Assert.Single(document.QuerySelectorAll(".article-toc"));
        var list = Assert.Single(tableOfContents.Children, element => element.LocalName == "ol");
        return list.QuerySelectorAll("li > a").ToArray();
    }
}
