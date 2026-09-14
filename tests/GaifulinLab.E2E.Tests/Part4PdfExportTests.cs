using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class Part4PdfExportTests(E2EEnvironment environment) : E2EPageTest
{
    private static readonly byte[] PdfBytes = Encoding.UTF8.GetBytes(
        "%PDF-1.4\n% Article PDF: Expected English text | image embedded\n%%EOF");

    [Fact]
    public async Task PdfDownload_DisablesDuplicateRequestAndDownloadsNamedFile()
    {
        var releasePdf = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pdfRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;
        await AuthenticateAdminAsync();
        await RoutePublicArticleAsync(async route =>
        {
            requestCount++;
            pdfRequested.TrySetResult();
            await releasePdf.Task;
            await PdfAsync(route);
        });
        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/pdf-article").ToString());
        var button = Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" });

        await button.ClickAsync();
        await pdfRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var preparingButton = Page.GetByRole(AriaRole.Button, new() { Name = "Preparing PDF…", Exact = true });
        await Expect(preparingButton).ToBeDisabledAsync();

        // A DOM click on a disabled button must not start a second public download request.
        await preparingButton.EvaluateAsync("element => element.click()");
        Assert.Equal(1, requestCount);

        var downloadTask = Page.WaitForDownloadAsync();
        releasePdf.TrySetResult();
        var download = await downloadTask;
        Assert.Equal("pdf-article.pdf", download.SuggestedFilename);
        Assert.Null(await download.FailureAsync());
        await using var stream = await download.CreateReadStreamAsync();
        using var content = new MemoryStream();
        await stream.CopyToAsync(content);
        Assert.Equal(PdfBytes, content.ToArray());
        Assert.StartsWith("%PDF-", Encoding.UTF8.GetString(content.ToArray()));
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" })).ToBeEnabledAsync();
    }

    [Fact]
    public async Task PdfDownload_QuotaExceeded_ShowsLocalizedMessageAndAllowsRetry()
    {
        var attempt = 0;
        await AuthenticateAdminAsync();
        await RoutePublicArticleAsync(async route =>
        {
            attempt++;
            if (attempt == 1)
            {
                await route.FulfillAsync(new() { Status = 429 });
                return;
            }

            await PdfAsync(route);
        });
        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/pdf-article").ToString());
        var button = Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" });

        await button.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert))
            .ToHaveTextAsync("Your monthly PDF download limit has been reached.");
        await Expect(button).ToBeEnabledAsync();

        var download = await Page.RunAndWaitForDownloadAsync(() => button.ClickAsync());
        Assert.Equal("pdf-article.pdf", download.SuggestedFilename);
        Assert.Null(await download.FailureAsync());
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        Assert.Equal(2, attempt);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task PdfDownload_FailedPublicResponse_ShowsUnavailableMessageAndAllowsRetry(int statusCode)
    {
        var attempt = 0;
        var downloadCount = 0;
        Page.Download += (_, _) => downloadCount++;
        await AuthenticateAdminAsync();
        await RoutePublicArticleAsync(async route =>
        {
            attempt++;
            if (attempt == 1)
            {
                await route.FulfillAsync(new() { Status = statusCode });
                return;
            }

            await PdfAsync(route);
        });
        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/pdf-article").ToString());
        var button = Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" });

        await button.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("The PDF is not available right now.");
        Assert.Equal(0, downloadCount);
        await Expect(button).ToBeEnabledAsync();

        var download = await Page.RunAndWaitForDownloadAsync(() => button.ClickAsync());
        Assert.Equal("pdf-article.pdf", download.SuggestedFilename);
        Assert.Null(await download.FailureAsync());
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        Assert.Equal(2, attempt);
    }

    private async Task AuthenticateAdminAsync()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{{\"sub\":\"pdf-admin\",\"role\":\"Admin\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Page.AddInitScriptAsync($"sessionStorage.setItem('gaifulinlab.admin.access_token','header.{payload}.signature');");
    }

    private async Task RoutePublicArticleAsync(Func<IRoute, Task> pdfResponse) =>
        await Page.RouteAsync("**/api/public/articles/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.EndsWith("/views", StringComparison.Ordinal))
            {
                await JsonAsync(route, new { viewCount = 1L });
                return;
            }

            if (path.EndsWith("/pdf", StringComparison.Ordinal))
            {
                await pdfResponse(route);
                return;
            }

            await JsonAsync(route, new
            {
                languageCode = "en", slug = "pdf-article", title = "PDF article", summary = "Export test",
                html = "<p>Expected English text</p><p>image embedded</p>",
                publishedAt = DateTimeOffset.UtcNow.AddDays(-1), updatedAt = DateTimeOffset.UtcNow,
                lastEditedAt = DateTimeOffset.UtcNow, authorDisplayName = "PDF Author",
                availableLocalizations = new[] { new { languageCode = "en", url = "/en/articles/pdf-article" } },
                topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<string>(), viewCount = 0L
            });
        });

    private static Task PdfAsync(IRoute route) => route.FulfillAsync(new()
    {
        Status = 200,
        ContentType = "application/pdf",
        BodyBytes = PdfBytes
    });

    private static Task JsonAsync(IRoute route, object body, int status = 200) => route.FulfillAsync(new()
    {
        Status = status,
        ContentType = "application/json",
        Body = JsonSerializer.Serialize(body, body.GetType())
    });
}
