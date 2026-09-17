using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace GaifulinLab.Infrastructure.Content;

/// <summary>
/// Normalizes the small, static SVG profile used in article illustrations.
/// SVG has element-specific URL and CSS rules which cannot be represented by
/// the article HTML sanitizer's global allow lists.
/// </summary>
internal sealed partial class ArticleSvgSanitizer
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly HashSet<string> AllowedElements =
    [
        "svg", "g", "defs", "path", "rect", "text", "tspan", "use", "clippath", "title", "desc"
    ];

    private static readonly HashSet<string> PresentationAttributes =
    [
        "fill", "stroke", "stroke-width", "stroke-opacity", "stroke-dasharray", "stroke-dashoffset",
        "stroke-linecap", "stroke-linejoin", "font-family", "font-size", "font-style", "font-weight", "text-anchor"
    ];

    private static readonly HashSet<string> NamedColors =
    ["black", "white", "red", "green", "blue", "gray", "grey", "yellow", "purple", "orange"];

    private static readonly XNamespace Svg = SvgNamespace;

    internal string? Sanitize(string svg, int ordinal, ISet<string> occupiedIds)
    {
        try
        {
            var root = Parse(svg);
            if (!string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase)
                || (root.Name.NamespaceName.Length != 0 && !string.Equals(root.Name.NamespaceName, SvgNamespace, StringComparison.Ordinal)))
            {
                return null;
            }

            var ids = BuildIdMap(root, ordinal, occupiedIds);
            var universalStyle = ReadUniversalStyle(root);
            var normalized = NormalizeElement(root, ids, universalStyle);

            return normalized?.ToString(SaveOptions.DisableFormatting);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static XElement Parse(string svg)
    {
        using var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });

        return XElement.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static Dictionary<string, string> BuildIdMap(XElement root, int ordinal, ISet<string> occupiedIds)
    {
        var sourceIds = new List<string>();

        foreach (var element in root.DescendantsAndSelf())
        {
            if (!HasSvgOrEmptyNamespace(element)
                || !AllowedElements.Contains(element.Name.LocalName.ToLowerInvariant()))
            {
                continue;
            }

            var rawId = element.Attribute("id")?.Value;
            if (rawId is null)
            {
                continue;
            }

            var id = NormalizeSourceId(rawId);
            if (!IsSafeId(id) || sourceIds.Contains(id, StringComparer.Ordinal))
            {
                continue;
            }

            sourceIds.Add(id);
        }

        var scope = ordinal + 1;
        string prefix;
        do
        {
            prefix = $"article-svg-{scope++}--";
        }
        while (sourceIds.Any(id => occupiedIds.Contains(prefix + id)));

        return sourceIds.ToDictionary(id => id, id => prefix + id, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ReadUniversalStyle(XElement root)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var style in root.Descendants().Where(element =>
                     HasSvgOrEmptyNamespace(element)
                     && string.Equals(element.Name.LocalName, "style", StringComparison.OrdinalIgnoreCase)))
        {
            var match = UniversalStyleRegex().Match(style.Value);
            if (!match.Success)
            {
                continue;
            }

            foreach (var (name, value) in ParseStyle(match.Groups[1].Value))
            {
                properties[name] = value;
            }
        }

        return properties;
    }

    private static XElement? NormalizeElement(
        XElement source,
        IReadOnlyDictionary<string, string> ids,
        IReadOnlyDictionary<string, string> universalStyle)
    {
        var name = source.Name.LocalName.ToLowerInvariant();
        if (!HasSvgOrEmptyNamespace(source) || !AllowedElements.Contains(name))
        {
            return null;
        }

        var result = new XElement(Svg + CanonicalElementName(name));
        var inlineStyle = ParseStyle(source.Attribute("style")?.Value);

        foreach (var (property, value) in universalStyle)
        {
            if (IsAllowedPresentationValue(property, value))
            {
                result.SetAttributeValue(property, value);
            }
        }

        foreach (var attribute in source.Attributes())
        {
            var attributeName = attribute.Name.LocalName;
            if (attributeName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(attributeName, "style", StringComparison.OrdinalIgnoreCase)
                || string.Equals(attributeName, "class", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(attributeName, "id", StringComparison.OrdinalIgnoreCase))
            {
                if (ids.TryGetValue(NormalizeSourceId(attribute.Value), out var normalizedId))
                {
                    result.SetAttributeValue("id", normalizedId);
                }

                continue;
            }

            if (string.Equals(attributeName, "href", StringComparison.OrdinalIgnoreCase))
            {
                if (name == "use" && TryNormalizeLocalReference(attribute.Value, ids, out var href))
                {
                    result.SetAttributeValue("href", href);
                }

                continue;
            }

            if (string.Equals(attributeName, "clip-path", StringComparison.OrdinalIgnoreCase))
            {
                if (TryNormalizeClipPath(attribute.Value, ids, out var clipPath))
                {
                    result.SetAttributeValue("clip-path", clipPath);
                }

                continue;
            }

            if (IsAllowedAttribute(name, attributeName) && IsSafeAttributeValue(attributeName, attribute.Value))
            {
                result.SetAttributeValue(attributeName, attribute.Value);
            }
        }

        foreach (var (property, value) in inlineStyle)
        {
            if (IsAllowedPresentationValue(property, value))
            {
                result.SetAttributeValue(property, value);
            }
        }

        foreach (var node in source.Nodes())
        {
            switch (node)
            {
                case XElement child:
                    var normalizedChild = NormalizeElement(child, ids, universalStyle);
                    if (normalizedChild is not null)
                    {
                        result.Add(normalizedChild);
                    }
                    break;
                case XText text when AllowsText(name):
                    result.Add(new XText(text.Value));
                    break;
            }
        }

        return result;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return [];
        }

        var properties = new List<KeyValuePair<string, string>>();
        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = declaration.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var name = declaration[..separator].Trim().ToLowerInvariant();
            var value = declaration[(separator + 1)..].Trim();
            if (PresentationAttributes.Contains(name))
            {
                properties.Add(new KeyValuePair<string, string>(name, value));
            }
        }

        return properties;
    }

    private static bool IsAllowedAttribute(string element, string attribute) =>
        attribute switch
        {
            "viewBox" => element == "svg",
            "width" or "height" => element is "svg" or "rect",
            "x" or "y" => element is "rect" or "text" or "tspan" or "use",
            "d" => element == "path",
            "transform" => element is "g" or "text" or "tspan" or "path" or "rect" or "use",
            _ => PresentationAttributes.Contains(attribute)
        };

    private static bool IsSafeAttributeValue(string attribute, string value) =>
        attribute switch
        {
            "viewBox" => IsViewBox(value),
            "width" or "height" or "x" or "y" => IsLength(value),
            "d" => PathDataRegex().IsMatch(value),
            "transform" => TransformRegex().IsMatch(value),
            _ when PresentationAttributes.Contains(attribute) => IsAllowedPresentationValue(attribute, value),
            _ => false
        };

    private static bool IsAllowedPresentationValue(string property, string value) =>
        property switch
        {
            "fill" or "stroke" => IsColor(value),
            "stroke-width" or "stroke-opacity" or "stroke-dashoffset" or "font-size" => IsLength(value),
            "stroke-dasharray" => string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
                || value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(IsLength),
            "stroke-linecap" => value is "butt" or "round" or "square",
            "stroke-linejoin" => value is "miter" or "round" or "bevel",
            "font-family" => FontFamilyRegex().IsMatch(value),
            "font-style" => value is "normal" or "italic" or "oblique",
            "font-weight" => value is "normal" or "bold" or "bolder" or "lighter" or "100" or "200" or "300" or "400" or "500" or "600" or "700" or "800" or "900",
            "text-anchor" => value is "start" or "middle" or "end",
            _ => false
        };

    private static bool IsColor(string value)
    {
        var color = value.Trim();
        return string.Equals(color, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(color, "currentColor", StringComparison.OrdinalIgnoreCase)
            || NamedColors.Contains(color)
            || HexColorRegex().IsMatch(color)
            || RgbColorRegex().IsMatch(color);
    }

    private static bool IsViewBox(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length == 4
        && value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(IsNumber);

    private static bool IsLength(string value)
    {
        var match = LengthRegex().Match(value);
        return match.Success && IsNumber(match.Groups[1].Value);
    }

    private static bool IsNumber(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number);

    private static bool TryNormalizeLocalReference(string value, IReadOnlyDictionary<string, string> ids, out string normalized)
    {
        normalized = string.Empty;
        if (LocalReferenceRegex().Match(value) is not { Success: true } match
            || !ids.TryGetValue(NormalizeSourceId(match.Groups[1].Value), out var target))
        {
            return false;
        }

        normalized = "#" + target;
        return true;
    }

    private static bool TryNormalizeClipPath(string value, IReadOnlyDictionary<string, string> ids, out string normalized)
    {
        normalized = string.Empty;
        var match = ClipPathRegex().Match(value);
        if (!match.Success || !ids.TryGetValue(NormalizeSourceId(match.Groups[1].Value), out var target))
        {
            return false;
        }

        normalized = $"url(#{target})";
        return true;
    }

    private static bool IsSafeId(string value) => SafeIdRegex().IsMatch(value);

    private static string NormalizeSourceId(string value)
    {
        return NormalizedIdRegex().Match(value) is { Success: true } match
            ? match.Groups[1].Value
            : value;
    }

    private static bool AllowsText(string element) => element is "text" or "tspan" or "title" or "desc";

    private static string CanonicalElementName(string name) => name == "clippath" ? "clipPath" : name;

    private static bool HasSvgOrEmptyNamespace(XElement element) =>
        element.Name.NamespaceName.Length == 0 || string.Equals(element.Name.NamespaceName, SvgNamespace, StringComparison.Ordinal);

    [GeneratedRegex("^\\s*\\*\\s*\\{(.*)\\}\\s*$", RegexOptions.Singleline)]
    private static partial Regex UniversalStyleRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.:-]*$")]
    private static partial Regex SafeIdRegex();

    [GeneratedRegex("^article-svg-\\d+--([A-Za-z_][A-Za-z0-9_.:-]*)$")]
    private static partial Regex NormalizedIdRegex();

    [GeneratedRegex("^#([A-Za-z_][A-Za-z0-9_.:-]*)$")]
    private static partial Regex LocalReferenceRegex();

    [GeneratedRegex("^url\\(\\s*#([A-Za-z_][A-Za-z0-9_.:-]*)\\s*\\)$", RegexOptions.IgnoreCase)]
    private static partial Regex ClipPathRegex();

    [GeneratedRegex("^[-+0-9.eE,\\sMmZzLlHhVvCcSsQqTtAa]+$")]
    private static partial Regex PathDataRegex();

    [GeneratedRegex("^\\s*(?:(?:translate|rotate|scale|matrix|skewX|skewY)\\(\\s*[-+0-9.eE,\\s]+\\s*\\)\\s*)+$")]
    private static partial Regex TransformRegex();

    [GeneratedRegex("^([-+]?(?:\\d+(?:\\.\\d*)?|\\.\\d+)(?:[eE][-+]?\\d+)?)(?:px|pt|%|em|ex)?$")]
    private static partial Regex LengthRegex();

    [GeneratedRegex("^#[0-9A-Fa-f]{3,8}$")]
    private static partial Regex HexColorRegex();

    [GeneratedRegex("^rgba?\\(\\s*(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\\s*,\\s*(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\\s*,\\s*(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])(?:\\s*,\\s*(?:0|0?\\.[0-9]+|1))?\\s*\\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbColorRegex();

    [GeneratedRegex("^[A-Za-z0-9 _,'\"-]+$")]
    private static partial Regex FontFamilyRegex();
}
