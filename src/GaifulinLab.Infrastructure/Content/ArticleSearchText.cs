using System.Text.RegularExpressions;
using Markdig;
using AngleSharp.Html.Parser;

namespace GaifulinLab.Infrastructure.Content;

internal static partial class ArticleSearchText
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static string Extract(string markdown)
    {
        var document = new HtmlParser().ParseDocument(Markdown.ToHtml(markdown, Pipeline));
        foreach (var hidden in document.QuerySelectorAll("script, style")) hidden.Remove();
        foreach (var image in document.QuerySelectorAll("img"))
            image.Replace(document.CreateTextNode(image.GetAttribute("alt") ?? ""));
        return Whitespace().Replace((document.Body?.TextContent ?? "")
            .Replace("\uE000", "").Replace("\uE001", ""), " ").Trim();
    }

    public static int ReadingMinutes(string text) => Math.Max(1, (Words().Matches(text).Count + 199) / 200);

    public static string NormalizeQuery(string text) => DotNet().Replace(
        CSharp().Replace(CPlusPlus().Replace(text.ToLowerInvariant(), "glcpp"), "glcsharp"), "gldotnet");

    [GeneratedRegex(@"\bc\+\+(?=$|\W)")]
    private static partial Regex CPlusPlus();

    [GeneratedRegex(@"\bc#(?=$|\W)")]
    private static partial Regex CSharp();

    [GeneratedRegex(@"\.net\b")]
    private static partial Regex DotNet();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();
}
