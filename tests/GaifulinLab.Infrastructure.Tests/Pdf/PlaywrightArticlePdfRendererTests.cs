using GaifulinLab.Infrastructure.Pdf;

namespace GaifulinLab.Infrastructure.Tests.Pdf;

public sealed class PlaywrightArticlePdfRendererTests
{
    [Fact]
    public void ForceDisplayIntegralLimits_AffectsOnlyDisplayMathematics()
    {
        const string html = """
            <p><span class="math">\(\int_0^1 x\,dx\)</span></p>
            <div class="math">$$\int_0^\infty e^{-x}\,dx$$</div>
            <pre>\int_0^1</pre>
            """;

        var normalized = PlaywrightArticlePdfRenderer.ForceDisplayIntegralLimits(html);

        Assert.Contains("""$$\int\limits_0^\infty e^{-x}\,dx$$""", normalized);
        Assert.Contains("""\(\int_0^1 x\,dx\)""", normalized);
        Assert.Contains("""<pre>\int_0^1</pre>""", normalized);
    }
}
