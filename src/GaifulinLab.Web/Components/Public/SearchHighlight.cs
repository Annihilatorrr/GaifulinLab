using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace GaifulinLab.Web.Components.Public;

// Database headline markers and literal matches render as encoded text, never HTML.
public sealed class SearchHighlight : ComponentBase
{
    [Parameter] public string Text { get; set; } = "";
    [Parameter] public string? Query { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var terms = (Query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(term => term.Length).Select(Regex.Escape).ToArray();
        var regex = terms.Length == 0 ? null : new Regex(string.Join('|', terms), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        var highlighted = false;
        foreach (var section in Regex.Split(Text, "([\uE000\uE001])"))
        {
            if (section == "\uE000") { highlighted = true; continue; }
            if (section == "\uE001") { highlighted = false; continue; }
            if (highlighted) { builder.OpenElement(0, "mark"); builder.AddContent(1, section); builder.CloseElement(); continue; }
            var offset = 0;
            if (regex is not null)
                foreach (Match match in regex.Matches(section))
                {
                    builder.AddContent(2, section[offset..match.Index]);
                    builder.OpenElement(3, "mark"); builder.AddContent(4, match.Value); builder.CloseElement();
                    offset = match.Index + match.Length;
                }
            builder.AddContent(5, section[offset..]);
        }
    }
}
