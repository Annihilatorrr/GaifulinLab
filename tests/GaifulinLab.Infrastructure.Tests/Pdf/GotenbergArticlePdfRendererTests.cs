using System.Net;
using GaifulinLab.Application.Common;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Infrastructure.Pdf;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaifulinLab.Infrastructure.Tests.Pdf;

public sealed class GotenbergArticlePdfRendererTests
{
    [Fact]
    public async Task RenderAsync_SendsPrintableArticleUrlAndReturnsPdf()
    {
        Uri? requestUri = null;
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestUri = request.RequestUri;
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("%PDF-1.7\ntest"u8.ToArray())
            };
        });
        var renderer = CreateRenderer(handler);

        var result = await renderer.RenderAsync("en", "understanding-fft", new ArticleTypography(1.5m, 0.4m), CancellationToken.None);

        Assert.Equal("%PDF-1.7\ntest"u8.ToArray(), result);
        Assert.Equal(
            "http://gotenberg:3000/forms/chromium/convert/url",
            requestUri?.AbsoluteUri);
        Assert.Contains("understanding-fft?pdf=1", requestBody);
        Assert.Contains("lineHeight=1.5", requestBody);
        Assert.Contains("blockSpacing=0.4", requestBody);
        Assert.Contains("waitForExpression", requestBody);
        Assert.Contains("data-pdf-ready", requestBody);
        Assert.Contains("printBackground", requestBody);
        Assert.Contains("preferCssPageSize", requestBody);
    }

    [Fact]
    public async Task RenderAsync_WhenResponseIsNotPdf_ThrowsPdfRenderingException()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not a PDF")
            }));
        var renderer = CreateRenderer(handler);

        var exception = await Assert.ThrowsAsync<PdfRenderingException>(() =>
            renderer.RenderAsync("en", "article", ArticleTypography.Default, CancellationToken.None));

        Assert.Equal("The PDF renderer returned an invalid document.", exception.Message);
    }

    private static GotenbergArticlePdfRenderer CreateRenderer(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://gotenberg:3000/"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        return new GotenbergArticlePdfRenderer(
            new StubHttpClientFactory(client),
            new ArticlePdfRendererSettings(new Uri("http://web/"), 1),
            NullLogger<GotenbergArticlePdfRenderer>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
