using GaifulinLab.Web.Components.Public;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaifulinLab.Web.Tests.Content;

public sealed class SearchHighlightTests
{
    [Fact]
    public async Task HighlightsDatabaseAndLiteralMatches_WhileEncodingUntrustedText()
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<SearchHighlight>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(SearchHighlight.Text)] = "<img src=x onerror=alert(1)> \uE000running\uE001 C++",
                [nameof(SearchHighlight.Query)] = "run C++"
            }));
            return output.ToHtmlString();
        });
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img", html);
        Assert.Contains("<mark>running</mark>", html);
        Assert.Contains("<mark>C++</mark>", System.Net.WebUtility.HtmlDecode(html));
    }
}
