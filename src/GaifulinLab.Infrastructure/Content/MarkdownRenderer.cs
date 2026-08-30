using GaifulinLab.Application.Content;
using Ganss.Xss;
using Markdig;
using System.Text;

namespace GaifulinLab.Infrastructure.Content;

public sealed class MarkdownRenderer : IMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseMathematics()
        .Build();

    private readonly HtmlSanitizer _sanitizer = CreateSanitizer();

    public string Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var html = Markdown.ToHtml(NormalizeMathDelimiters(markdown), Pipeline);
        return _sanitizer.Sanitize(html);
    }

    private static string NormalizeMathDelimiters(string markdown)
    {
        var normalizedLines = markdown.ReplaceLineEndings("\n").Split('\n');
        var result = new StringBuilder(markdown.Length);
        var insideFence = false;
        var fenceMarker = '\0';
        var fenceLength = 0;

        for (var index = 0; index < normalizedLines.Length; index++)
        {
            var line = normalizedLines[index];
            if (TryReadFence(line, out var marker, out var length))
            {
                if (!insideFence)
                {
                    insideFence = true;
                    fenceMarker = marker;
                    fenceLength = length;
                }
                else if (marker == fenceMarker && length >= fenceLength)
                {
                    insideFence = false;
                }

                result.Append(line);
            }
            else
            {
                result.Append(insideFence ? line : NormalizeMathDelimitersInLine(line));
            }

            if (index < normalizedLines.Length - 1)
            {
                result.Append('\n');
            }
        }

        return result.ToString();
    }

    private static string NormalizeMathDelimitersInLine(string line)
    {
        var result = new StringBuilder(line.Length);
        var inlineCodeDelimiterLength = 0;

        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '`')
            {
                var delimiterLength = 1;
                while (index + delimiterLength < line.Length && line[index + delimiterLength] == '`')
                {
                    delimiterLength++;
                }

                result.Append('`', delimiterLength);
                if (inlineCodeDelimiterLength == 0)
                {
                    inlineCodeDelimiterLength = delimiterLength;
                }
                else if (inlineCodeDelimiterLength == delimiterLength)
                {
                    inlineCodeDelimiterLength = 0;
                }

                index += delimiterLength - 1;
                continue;
            }

            if (inlineCodeDelimiterLength == 0
                && line[index] == '\\'
                && index + 1 < line.Length)
            {
                var replacement = line[index + 1] switch
                {
                    '(' or ')' => "$",
                    '[' or ']' => "$$",
                    _ => null
                };
                if (replacement is not null)
                {
                    result.Append(replacement);
                    index++;
                    continue;
                }
            }

            result.Append(line[index]);
        }

        return result.ToString();
    }

    private static bool TryReadFence(string line, out char marker, out int length)
    {
        var index = 0;
        while (index < line.Length && index < 4 && line[index] == ' ')
        {
            index++;
        }

        marker = index < line.Length ? line[index] : '\0';
        if (index > 3 || marker is not ('`' or '~'))
        {
            length = 0;
            return false;
        }

        length = 1;
        while (index + length < line.Length && line[index + length] == marker)
        {
            length++;
        }

        return length >= 3;
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
