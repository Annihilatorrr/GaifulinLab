using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleImageWorkflowTests : PageTest
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
        await Page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "e2e-diagram.png",
            MimeType = "image/png",
            Buffer = OnePixelPng
        });

        var markdown = Page.Locator("#article-markdown");
        await Expect(markdown).ToHaveValueAsync(new Regex(
            "!\\[e2e-diagram\\]\\(/media/[0-9a-f-]{36}\\)",
            RegexOptions.None));

        var previewImage = Page.Locator("article.article-preview img");
        await Expect(previewImage).ToBeVisibleAsync();
        await Expect(previewImage).ToHaveAttributeAsync("src", new Regex("^/media/[0-9a-f-]{36}$"));

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/[0-9a-f-]{36}$"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Open article" })).ToBeVisibleAsync();

        await AssertPublicImageLoadsAsync(Page, new Uri(_environment.BaseUri, publicPath));
        await AssertPdfDownloadsAsync(Page, slug);
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

    private static async Task AssertPdfDownloadsAsync(IPage page, string slug)
    {
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("/api/public/articles/", StringComparison.Ordinal)
            && response.Url.Contains("/pdf", StringComparison.Ordinal));
        var download = await page.RunAndWaitForDownloadAsync(
            () => page.GetByRole(AriaRole.Link, new() { Name = "Download PDF" }).ClickAsync());
        var response = await responseTask;

        Assert.Equal($"{slug}.pdf", download.SuggestedFilename);
        Assert.StartsWith(
            "application/pdf",
            response.Headers["content-type"],
            StringComparison.OrdinalIgnoreCase);
        var path = await download.PathAsync();
        Assert.False(string.IsNullOrWhiteSpace(path));

        var header = new byte[5];
        await using var stream = File.OpenRead(path);
        Assert.Equal(header.Length, await stream.ReadAsync(header));
        Assert.Equal("%PDF-"u8.ToArray(), header);
    }
}
