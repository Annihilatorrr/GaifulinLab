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
}
