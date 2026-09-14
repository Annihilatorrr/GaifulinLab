using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleImageWorkflowTests : E2EPageTest
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly E2EEnvironment _environment;

    public ArticleImageWorkflowTests(E2EEnvironment environment)
    {
        _environment = environment;
    }

    [Fact]
    public async Task UploadImage_SavePublishAndDownloadPdf_ContentIsAvailableOnThePrimarySite()
    {
        await _environment.EnsurePdfWorkerAsync();
        var (login, password) = _environment.GetAdminCredentials();
        var uniqueId = Guid.NewGuid().ToString("N");
        var title = $"E2E image article {uniqueId}";
        var slug = $"e2e-image-article-{uniqueId}";
        var publicPath = $"/en/articles/{slug}";

        await Page.GotoAsync(new Uri(_environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles$"));

        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/new$"));

        await Page.GetByLabel("Article title").FillAsync(title);
        await Expect(Page.GetByPlaceholder("article-slug")).ToHaveValueAsync(slug);
        await Page.Locator(".image-upload input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "e2e-diagram.png",
            MimeType = "image/png",
            Buffer = OnePixelPng
        });

        var html = Page.Locator("#article-html");
        await Expect(html).ToHaveValueAsync(new Regex(
            "!\\[e2e-diagram\\]\\(/media/[0-9a-f-]{36}\\)",
            RegexOptions.None));

        var previewImage = Page.Locator("article.article-preview img");
        await Expect(previewImage).ToBeVisibleAsync();
        var previewImageSource = await previewImage.GetAttributeAsync("src");
        Assert.NotNull(previewImageSource);
        var previewImageUri = new Uri(previewImageSource);
        Assert.Equal(_environment.ApiBaseUri.GetLeftPart(UriPartial.Authority), previewImageUri.GetLeftPart(UriPartial.Authority));
        Assert.Matches(new Regex("^/media/[0-9a-f-]{36}$"), previewImageUri.AbsolutePath);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/[0-9a-f-]{36}$"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await Page.GetByLabel("More article actions").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Open article" })).ToBeVisibleAsync();

        await ExportPdfAndAssertDownloadAsync(slug);
        await AssertPublicImageLoadsAsync(Page, new Uri(_environment.BaseUri, publicPath));
        await AssertPublicPdfAccessAsync(publicPath, slug);
    }

    private static async Task AssertPublicImageLoadsAsync(IPage page, Uri publicArticleUri)
    {
        await page.GotoAsync(publicArticleUri.ToString());
        var image = page.Locator("article.article-body img");
        await image.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            "image => image.complete && image.naturalWidth > 0",
            await image.ElementHandleAsync());
        var width = await image.EvaluateAsync<int>("element => element.naturalWidth");
        Assert.True(width > 0, "The public article image was rendered but could not be loaded.");
    }

    private async Task ExportPdfAndAssertDownloadAsync(string slug)
    {
        var queuedResponseTask = Page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("/api/admin/articles/", StringComparison.Ordinal)
            && response.Url.Contains("/pdf-exports", StringComparison.Ordinal));
        var responseTask = Page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("/api/admin/pdf-exports/", StringComparison.Ordinal)
            && response.Url.Contains("/download", StringComparison.Ordinal));
        var downloadTask = Page.WaitForDownloadAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Export PDF" }).ClickAsync();
        var queuedResponse = await queuedResponseTask;
        var response = await responseTask;
        var download = await downloadTask;

        Assert.Equal(202, queuedResponse.Status);
        await AssertPdfDownloadAsync(response, download, slug);
    }

    private async Task AssertPublicPdfAccessAsync(string publicPath, string slug)
    {
        await Page.EvaluateAsync("sessionStorage.clear()");
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" })).ToHaveCountAsync(0);

        var login = $"pdf-reader-{Guid.NewGuid():N}@example.com";
        const string password = "Strong-password-1!";
        await Page.GotoAsync(new Uri(_environment.BaseUri, "/register").ToString());
        await Page.GetByLabel("Display name").FillAsync("PDF Reader");
        await Page.GetByLabel("Email").FillAsync(login);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await Page.GetByLabel("Confirm password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create an account" }).ClickAsync();

        await Page.GotoAsync(new Uri(_environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        await Page.GotoAsync(new Uri(_environment.BaseUri, publicPath).ToString());
        var button = Page.GetByRole(AriaRole.Button, new() { Name = "Download PDF" });
        await Expect(button).ToBeVisibleAsync();
        var responseTask = Page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains($"/api/public/articles/en/{slug}/pdf", StringComparison.Ordinal));
        var downloadTask = Page.WaitForDownloadAsync();
        await button.ClickAsync();
        var response = await responseTask;
        var download = await downloadTask;

        await AssertPdfDownloadAsync(response, download, slug);
    }

    private static async Task AssertPdfDownloadAsync(IResponse response, IDownload download, string slug)
    {
        Assert.StartsWith(
            "application/pdf",
            response.Headers["content-type"],
            StringComparison.OrdinalIgnoreCase);
        var content = await response.BodyAsync();
        Assert.True(content.AsSpan().StartsWith("%PDF-"u8));
        Assert.Equal($"{slug}.pdf", download.SuggestedFilename);
        Assert.Null(await download.FailureAsync());
        await using var downloadedContent = await download.CreateReadStreamAsync();
        var header = new byte[5];
        Assert.Equal(header.Length, await downloadedContent.ReadAsync(header));
        Assert.Equal("%PDF-"u8.ToArray(), header);
    }
}
