using GaifulinLab.Infrastructure.Pdf;

namespace GaifulinLab.Infrastructure.Tests.Pdf;

public sealed class PlaywrightArticlePdfRendererTests
{
    [Fact]
    public async Task AwaitWithContextCancellationAsync_WhenRenderingTimesOut_ClosesTheContext()
    {
        using var cancellation = new CancellationTokenSource();
        var render = new TaskCompletionSource<byte[]>();
        var contextClosed = false;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PlaywrightArticlePdfRenderer.AwaitWithContextCancellationAsync(
                render.Task,
                () =>
                {
                    contextClosed = true;
                    return Task.CompletedTask;
                },
                cancellation.Token));

        Assert.True(contextClosed);
    }

    [Fact]
    public async Task AwaitWithContextCancellationAsync_WhenRenderingCompletes_DoesNotCloseTheContext()
    {
        var contextClosed = false;

        var result = await PlaywrightArticlePdfRenderer.AwaitWithContextCancellationAsync(
            Task.FromResult("%PDF-"u8.ToArray()),
            () =>
            {
                contextClosed = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal("%PDF-"u8.ToArray(), result);
        Assert.False(contextClosed);
    }

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
