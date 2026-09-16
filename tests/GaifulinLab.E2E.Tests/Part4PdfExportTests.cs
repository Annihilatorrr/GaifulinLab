using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    [Fact]
    public async Task GuestSigningInForPdf_ReturnsToTheOriginalArticleAndShowsDownloadButton()
    {
        var article = await SeedPublishedArticleAsync();
        var articlePath = $"/en/articles/{article.Slug}";
        var (login, password) = environment.GetAdminCredentials();

        await Page.GotoAsync(new Uri(environment.BaseUri, articlePath).ToString());
        var signInLink = Page.Locator(".article-meta")
            .GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true });

        await signInLink.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();

        // The PDF call to action must preserve the decoded local article path through login.
        Assert.Equal(articlePath, GetQueryParameter(new Uri(Page.Url), "returnUrl"));

        await SignInAsync(login, password);

        await AssertLocalPathAsync(articlePath);
        // A successful sign-in changes this article's PDF prompt into its download action.
        await Expect(Page.Locator(".article-meta")
            .GetByRole(AriaRole.Button, new() { Name = "Download PDF", Exact = true }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task GuestRegisteringForPdf_ReturnsToTheOriginalArticleAndShowsDownloadButton()
    {
        var article = await SeedPublishedArticleAsync();
        var articlePath = $"/en/articles/{article.Slug}";
        var login = $"pdf-return-{Guid.NewGuid():N}@example.com";
        const string password = "Strong-password-1!";

        await Page.GotoAsync(new Uri(environment.BaseUri, articlePath).ToString());
        var registerLink = Page.Locator(".article-meta")
            .GetByRole(AriaRole.Link, new() { Name = "register", Exact = true });

        await registerLink.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Create an account", Exact = true }))
            .ToBeVisibleAsync();

        // Registration receives the same decoded local destination as direct sign-in.
        Assert.Equal(articlePath, GetQueryParameter(new Uri(Page.Url), "returnUrl"));

        await RegisterFromCurrentPageAsync(login, "PDF Return Reader", password);
        var signInLink = Page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true });
        var signInHref = await signInLink.GetAttributeAsync("href") ?? string.Empty;

        // The post-registration sign-in link must carry that destination onward.
        Assert.Equal(articlePath, GetQueryParameter(new Uri(environment.BaseUri, signInHref), "returnUrl"));

        await signInLink.ClickAsync();
        await SignInAsync(login, password);

        await AssertLocalPathAsync(articlePath);
        await Expect(Page.Locator(".article-meta")
            .GetByRole(AriaRole.Button, new() { Name = "Download PDF", Exact = true }))
            .ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("https://attacker.example/return")]
    [InlineData("//attacker.example/return")]
    [InlineData("/\\attacker.example/return")]
    public async Task SignIn_RejectsUnsafeReturnUrlAndFallsBackToArticleList(string unsafeReturnUrl)
    {
        var (login, password) = environment.GetAdminCredentials();

        await Page.GotoAsync(new Uri(
            environment.BaseUri,
            $"/admin/login?returnUrl={Uri.EscapeDataString(unsafeReturnUrl)}").ToString());
        await SignInAsync(login, password);

        await AssertLocalPathAsync("/admin/articles");
    }

    [Theory]
    [InlineData("https://attacker.example/return")]
    [InlineData("//attacker.example/return")]
    [InlineData("/\\attacker.example/return")]
    public async Task Registration_RejectsUnsafeReturnUrlBeforeAndAfterSignIn(string unsafeReturnUrl)
    {
        var login = $"unsafe-return-{Guid.NewGuid():N}@example.com";
        const string password = "Strong-password-1!";

        await Page.GotoAsync(new Uri(
            environment.BaseUri,
            $"/register?returnUrl={Uri.EscapeDataString(unsafeReturnUrl)}").ToString());
        await RegisterFromCurrentPageAsync(login, "Unsafe Return Reader", password);

        var signInLink = Page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true });

        // An unsafe target must not survive registration as a sign-in return parameter.
        Assert.Equal("/admin/login", await signInLink.GetAttributeAsync("href"));

        await signInLink.ClickAsync();
        await SignInAsync(login, password);

        await AssertLocalPathAsync("/admin/articles");
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

    private Task<SeededArticle> SeedPublishedArticleAsync()
    {
        var uniqueId = Guid.NewGuid().ToString("N");
        return environment.SeedPublishedArticleAsync(
            $"PDF return article {uniqueId}",
            $"pdf-return-{uniqueId}",
            "<p>PDF return article.</p>");
    }

    private async Task RegisterFromCurrentPageAsync(string login, string displayName, string password)
    {
        await Page.GetByLabel("Display name").FillAsync(displayName);
        await Page.GetByLabel("Email").FillAsync(login);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await Page.GetByLabel("Confirm password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create an account", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Welcome, {displayName}", Exact = true }))
            .ToBeVisibleAsync();
    }

    private async Task SignInAsync(string login, string password)
    {
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
    }

    private async Task AssertLocalPathAsync(string expectedPath)
    {
        await Expect(Page).ToHaveURLAsync(new Regex($"{Regex.Escape(expectedPath)}$"));
        var actualUrl = new Uri(Page.Url);
        Assert.Equal(environment.BaseUri.GetLeftPart(UriPartial.Authority), actualUrl.GetLeftPart(UriPartial.Authority));
        Assert.Equal(expectedPath, actualUrl.AbsolutePath);
    }

    private static string? GetQueryParameter(Uri uri, string name) => uri.Query
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .Where(part => part.Length == 2 && string.Equals(Uri.UnescapeDataString(part[0]), name, StringComparison.Ordinal))
        .Select(part => Uri.UnescapeDataString(part[1]))
        .SingleOrDefault();
}
