using System.Net;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Web.Content;

namespace GaifulinLab.Web.Tests.Content;

public sealed class PublicContentClientTests
{
    [Fact]
    public void GetArticlePdfUrl_UsesConfiguredApiOriginAndEscapesRouteValues()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5180/")
        };
        var client = new PublicContentClient(httpClient, new Uri("http://host.docker.internal:5180/"));

        var url = client.GetArticlePdfUrl(
            "en",
            "article with spaces",
            new ArticleTypography(1.5m, 0.4m));

        Assert.Equal(
            "http://localhost:5180/api/public/articles/en/article%20with%20spaces/pdf?lineHeight=1.5&blockSpacing=0.4",
            url);
    }

    [Fact]
    public async Task GetArticleAsync_ForPdf_UsesTheContainerReachableApiOrigin()
    {
        Uri? requestUri = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("http://localhost:5180/")
        };
        var client = new PublicContentClient(httpClient, new Uri("http://host.docker.internal:5180/"));

        var article = await client.GetArticleAsync("en", "test article", forPdf: true);

        Assert.Null(article);
        Assert.Equal(
            "http://host.docker.internal:5180/api/public/articles/en/test%20article",
            requestUri?.AbsoluteUri);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
