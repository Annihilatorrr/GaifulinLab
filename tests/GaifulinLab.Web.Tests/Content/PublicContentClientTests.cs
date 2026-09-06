using System.Net;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Web.Content;

namespace GaifulinLab.Web.Tests.Content;

public sealed class PublicContentClientTests
{
    [Fact]
    public async Task CreateArticlePdfExportAsync_PostsTypographyAndReturnsQueuedExport()
    {
        HttpRequestMessage? request = null;
        var exportId = Guid.NewGuid();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(message =>
        {
            request = message;
            return JsonResponse(new PdfExportStatusDto(exportId, "queued", null, null), HttpStatusCode.Accepted);
        }))
        {
            BaseAddress = new Uri("http://localhost:5180/")
        };
        var client = new PublicContentClient(httpClient);

        var export = await client.CreateArticlePdfExportAsync(
            "en",
            "article with spaces",
            new ArticleTypography(1.5m, 0.4m));

        Assert.Equal(exportId, export.Id);
        Assert.Equal(HttpMethod.Post, request?.Method);
        Assert.Equal(
            "/api/public/articles/en/article%20with%20spaces/pdf-exports?lineHeight=1.5&blockSpacing=0.4",
            request?.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task GetPdfExportAsync_GetsExportStatus()
    {
        Uri? requestUri = null;
        var exportId = Guid.NewGuid();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(message =>
        {
            requestUri = message.RequestUri;
            return JsonResponse(new PdfExportStatusDto(
                exportId,
                "completed",
                null,
                $"/api/public/pdf-exports/{exportId}/download"),
                HttpStatusCode.OK);
        }))
        {
            BaseAddress = new Uri("http://localhost:5180/")
        };
        var client = new PublicContentClient(httpClient);

        var export = await client.GetPdfExportAsync(exportId);

        Assert.Equal("completed", export.Status);
        Assert.Equal(
            $"/api/public/pdf-exports/{exportId}",
            requestUri?.PathAndQuery);
    }

    [Fact]
    public async Task RecordArticleViewAsync_PostsAndReturnsTheUpdatedCount()
    {
        HttpRequestMessage? request = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(message =>
        {
            request = message;
            return JsonResponse(new ArticleViewCountDto(7), HttpStatusCode.OK);
        }))
        {
            BaseAddress = new Uri("http://localhost:5180/")
        };
        var client = new PublicContentClient(httpClient);

        var view = await client.RecordArticleViewAsync("en", "article with spaces");

        Assert.Equal(7, view.ViewCount);
        Assert.Equal(HttpMethod.Post, request?.Method);
        Assert.Equal(
            "/api/public/articles/en/article%20with%20spaces/views",
            request?.RequestUri?.PathAndQuery);
    }

    [Fact]
    public void ResolveApiUrl_UsesTheApiOriginForRelativeUrls()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5180/")
        };
        var client = new PublicContentClient(httpClient);

        var url = client.ResolveApiUrl("/api/public/pdf-exports/123/download");

        Assert.Equal("http://localhost:5180/api/public/pdf-exports/123/download", url);
    }

    private static HttpResponseMessage JsonResponse<T>(T value, HttpStatusCode statusCode) =>
        new(statusCode)
        {
            Content = JsonContent.Create(value)
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
