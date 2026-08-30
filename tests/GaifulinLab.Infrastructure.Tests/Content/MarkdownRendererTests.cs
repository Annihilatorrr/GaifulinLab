using GaifulinLab.Infrastructure.Content;

namespace GaifulinLab.Infrastructure.Tests.Content;

public sealed class MarkdownRendererTests
{
    private readonly MarkdownRenderer _renderer = new();

    [Fact]
    public void Render_UsesAdvancedMarkdownExtensions()
    {
        const string markdown = """
            # Heading

            | Name | Value |
            | --- | ---: |
            | FFT | **Fast** |
            """;

        var html = _renderer.Render(markdown);

        Assert.Contains("<h1", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<strong>Fast</strong>", html);
    }

    [Fact]
    public void Render_RemovesExecutableHtmlAndUnsafeUris()
    {
        const string markdown = """
            # Safe heading

            <script>alert('xss')</script>
            <img src="/media/image.png" onerror="alert('xss')" style="display:none">
            <a href="javascript:alert('xss')">unsafe link</a>
            """;

        var html = _renderer.Render(markdown);

        Assert.Contains("Safe heading", html);
        Assert.Contains("src=\"/media/image.png\"", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }
}
