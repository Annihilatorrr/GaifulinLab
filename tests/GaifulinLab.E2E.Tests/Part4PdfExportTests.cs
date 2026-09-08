using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class Part4PdfExportTests(E2EEnvironment environment) : PageTest
{
    private static readonly Guid ExportId = Guid.Parse("b871be57-c893-4219-9859-8bec498426d8");
    private static readonly byte[] PdfBytes = Encoding.UTF8.GetBytes(
        "%PDF-1.4\n% Article PDF: Expected English text | Кириллица | formula x^2 | image embedded\n%%EOF");

    [Fact]
    public async Task PdfDownload_ShowsPreparingPreventsDuplicateJobAndDownloadsNamedFile()
    {
        var releaseExport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exportStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var postCount = 0;
        await AuthenticateAdminAsync();
        await RoutePublicArticleAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.EndsWith("/pdf-exports", StringComparison.Ordinal) && route.Request.Method == "POST")
            {
                postCount++;
                exportStarted.TrySetResult();
                await releaseExport.Task;
                await JsonAsync(route, Status("completed", download: true), 202);
                return;
            }
            if (path.EndsWith("/download", StringComparison.Ordinal)) { await PdfAsync(route); return; }
            await JsonAsync(route, Status("completed", download: true));
        });
        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/pdf-article").ToString());
        var button = Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" });

        await button.ClickAsync();
        await exportStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Preparing PDF…" })).ToBeDisabledAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preparing PDF…" })
            .EvaluateAsync("element => element.click()");
        Assert.Equal(1, postCount);

        var downloadTask = Page.WaitForDownloadAsync();
        releaseExport.TrySetResult();
        var download = await downloadTask;
        Assert.Equal("pdf-article.pdf", download.SuggestedFilename);
        Assert.Null(await download.FailureAsync());
        await using var stream = await download.CreateReadStreamAsync();
        using var content = new MemoryStream();
        await stream.CopyToAsync(content);
        Assert.Equal(PdfBytes, content.ToArray());
        Assert.StartsWith("%PDF-", Encoding.UTF8.GetString(content.ToArray()));
        Assert.Contains("Кириллица", Encoding.UTF8.GetString(content.ToArray()));
        Assert.Contains("formula x^2", Encoding.UTF8.GetString(content.ToArray()));
        Assert.Contains("image embedded", Encoding.UTF8.GetString(content.ToArray()));
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" })).ToBeEnabledAsync();
    }

    [Theory]
    [InlineData("start", "Unable to start the PDF export.")]
    [InlineData("poll", "Unable to start the PDF export.")]
    [InlineData("download", "Unable to start the PDF export.")]
    [InlineData("failed", "Renderer could not create the PDF.")]
    public async Task PdfFailures_LeaveWaitingStateAndRetrySuccessfully(string failure, string expectedMessage)
    {
        var attempt = 0;
        await AuthenticateAdminAsync();
        await RoutePublicArticleAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.EndsWith("/pdf-exports", StringComparison.Ordinal) && route.Request.Method == "POST")
            {
                attempt++;
                if (attempt == 1 && failure == "start") { await route.FulfillAsync(new() { Status = 500 }); return; }
                if (attempt == 1 && failure == "failed")
                {
                    await JsonAsync(route, Status("failed", error: "Renderer could not create the PDF."), 202);
                    return;
                }
                await JsonAsync(route, Status(attempt == 1 && failure == "poll" ? "queued" : "completed", download: attempt > 1 || failure == "download"), 202);
                return;
            }
            if (path.EndsWith("/download", StringComparison.Ordinal))
            {
                if (attempt == 1 && failure == "download") { await route.FulfillAsync(new() { Status = 500 }); return; }
                await PdfAsync(route);
                return;
            }
            if (attempt == 1 && failure == "poll") { await route.FulfillAsync(new() { Status = 500 }); return; }
            await JsonAsync(route, Status("completed", download: true));
        });
        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/pdf-article").ToString());
        var button = Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" });

        await button.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync(expectedMessage);
        await Expect(button).ToBeEnabledAsync();

        var download = await Page.RunAndWaitForDownloadAsync(() => button.ClickAsync());
        Assert.Equal("pdf-article.pdf", download.SuggestedFilename);
        Assert.Null(await download.FailureAsync());
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task PdfPollingTimeout_ShowsTimeoutAndAllowsAnotherAttempt()
    {
        var complete = false;
        await AuthenticateAdminAsync();
        await RoutePublicArticleAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.EndsWith("/pdf-exports", StringComparison.Ordinal) && route.Request.Method == "POST")
            {
                await JsonAsync(route, Status(complete ? "completed" : "queued", download: complete), 202);
                return;
            }
            if (path.EndsWith("/download", StringComparison.Ordinal)) { await PdfAsync(route); return; }
            await JsonAsync(route, Status("queued"));
        });
        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/pdf-article").ToString());
        await Page.Clock.InstallAsync();
        var button = Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" });
        await button.ClickAsync();
        for (var second = 0; second < 61; second++)
        {
            await Page.Clock.RunForAsync(1_000);
            await Task.Delay(10);
        }
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("taking longer than expected");
        await Expect(button).ToBeEnabledAsync();

        complete = true;
        var download = await Page.RunAndWaitForDownloadAsync(() => button.ClickAsync());
        Assert.Equal("pdf-article.pdf", download.SuggestedFilename);
    }

    [Fact]
    public async Task PdfExport_WhenArticleBecomesUnavailable_DoesNotDownload()
    {
        var downloadCount = 0;
        Page.Download += (_, _) => downloadCount++;
        await AuthenticateAdminAsync();
        await RoutePublicArticleAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.EndsWith("/pdf-exports", StringComparison.Ordinal) && route.Request.Method == "POST")
            {
                await JsonAsync(route, Status("queued"), 202);
                return;
            }
            await route.FulfillAsync(new() { Status = 404 });
        });
        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/pdf-article").ToString());

        await Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Unable to start the PDF export.");
        Assert.Equal(0, downloadCount);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" })).ToBeEnabledAsync();
    }

    private async Task AuthenticateAdminAsync()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{{\"sub\":\"pdf-admin\",\"role\":\"Admin\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Page.AddInitScriptAsync($"sessionStorage.setItem('gaifulinlab.admin.access_token','header.{payload}.signature');");
    }

    private async Task RoutePublicArticleAsync() =>
        await Page.RouteAsync("**/api/public/articles/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.EndsWith("/views", StringComparison.Ordinal)) { await JsonAsync(route, new { viewCount = 1L }); return; }
            await JsonAsync(route, new
            {
                languageCode = "en", slug = "pdf-article", title = "PDF article", summary = "Export test",
                html = "<p>Expected English text</p><p>Кириллица</p><span class=\"math\">\\(x^2\\)</span>",
                publishedAt = DateTimeOffset.UtcNow.AddDays(-1), updatedAt = DateTimeOffset.UtcNow,
                lastEditedAt = DateTimeOffset.UtcNow, authorDisplayName = "PDF Author",
                availableLocalizations = new[] { new { languageCode = "en", url = "/en/articles/pdf-article" } },
                topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<string>(), viewCount = 0L
            });
        });

    private static object Status(string status, bool download = false, string? error = null) => new
    {
        id = ExportId,
        status,
        errorMessage = error,
        downloadUrl = download ? $"/api/admin/pdf-exports/{ExportId}/download" : null
    };

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
