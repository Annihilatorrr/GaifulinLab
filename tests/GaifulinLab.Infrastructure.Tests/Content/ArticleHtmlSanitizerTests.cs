using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using GaifulinLab.Infrastructure.Content;
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
                article-formula--accent article-figure--placeholder article-figure--center article-figure--left
                article-figure--right article-figure--small article-figure--wide article-figure__canvas
                article-figure__index article-clear article-next article-next__eyebrow">Content</aside>
            """;
        string[] expectedClasses =
        [
            "article-summary", "article-toc", "article-example", "article-example__title",
            "article-callout--note", "article-callout--important", "article-callout--tip",
            "article-callout--deep", "article-formula--accent", "article-figure--placeholder",
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
