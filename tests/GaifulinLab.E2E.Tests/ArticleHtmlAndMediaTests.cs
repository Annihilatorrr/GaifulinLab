using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaifulinLab.Infrastructure.Tests.Content;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleHtmlAndMediaTests(E2EEnvironment environment) : E2EPageTest
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task EmptyTaxonomy_ShowsEmptyTopicAndSeriesStates()
    {
        await AuthenticateAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy")
            {
                await JsonAsync(route, EmptyTaxonomy());
                return;
            }

            await JsonAsync(route, path == "/api/admin/html/preview"
                ? new { html = "" }
                : new { });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        await Page.GetByText("+ Add topic", new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByText("No topics created yet.", new() { Exact = true })).ToBeVisibleAsync();
        await Page.GetByText("+ Add series", new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByText("No series created yet.", new() { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TaxonomyNames_FollowEditorLanguageWithFallbackAndKeepAssignments()
    {
        var articleId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var fallbackTopicId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        await AuthenticateAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy")
            {
                await JsonAsync(route, new
                {
                    topics = new object[]
                    {
                        TaxonomyTopic(topicId, ("en", "Engineering"), ("ru", "Инженерия")),
                        TaxonomyTopic(fallbackTopicId, ("en", "English fallback"))
                    },
                    series = new object[]
                    {
                        TaxonomySeries(seriesId, ("en", "Signal notes"), ("ru", "Заметки о сигналах"))
                    },
                    tags = Array.Empty<object>()
                });
                return;
            }

            if (path == $"/api/admin/articles/{articleId}" && route.Request.Method == "GET")
            {
                await JsonAsync(route, ArticleDetails(articleId, topicId, fallbackTopicId, seriesId));
                return;
            }

            if (path == "/api/admin/html/preview")
            {
                await JsonAsync(route, new { html = "<p>Body</p>" });
                return;
            }

            await JsonAsync(route, new { });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{articleId}").ToString());
        await Page.GetByText("Topics (2)", new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByLabel("Engineering", new() { Exact = true })).ToBeCheckedAsync();
        await Expect(Page.GetByLabel("English fallback", new() { Exact = true })).ToBeCheckedAsync();
        await Page.GetByText("Series (1)", new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByLabel("Signal notes", new() { Exact = true })).ToBeCheckedAsync();

        await Page.GetByLabel("Article language").SelectOptionAsync("ru");

        await Expect(Page.GetByLabel("Инженерия", new() { Exact = true })).ToBeCheckedAsync();
        await Expect(Page.GetByLabel("English fallback", new() { Exact = true })).ToBeCheckedAsync();
        await Expect(Page.GetByLabel("Заметки о сигналах", new() { Exact = true })).ToBeCheckedAsync();
        await Expect(Page.GetByText("Topics (2)", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Series (1)", new() { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Preview_IgnoresLateResponseAndClearingHtmlShowsEmptyState()
    {
        var firstRequestArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await AuthenticateAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/html/preview")
            {
                using var request = JsonDocument.Parse(route.Request.PostData!);
                var html = request.RootElement.GetProperty("html").GetString()!;
                if (html == "First response")
                {
                    firstRequestArrived.TrySetResult();
                    await releaseFirst.Task;
                }

                await JsonAsync(route, new { html = $"<p>{html}</p>" });
                return;
            }

            await JsonAsync(route, new { });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        var html = Page.GetByLabel("Article Html");
        await html.FillAsync("First response");
        await firstRequestArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await html.FillAsync("Current response");
        await Expect(Page.Locator("article.article-preview")).ToHaveTextAsync("Current response");
        releaseFirst.TrySetResult();
        await Page.WaitForTimeoutAsync(250);
        await Expect(Page.Locator("article.article-preview")).ToHaveTextAsync("Current response");

        await html.FillAsync("");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Nothing to preview yet" })).ToBeVisibleAsync();
        await Expect(Page.Locator("article.article-preview")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Preview_AfterRenderingFailureKeepsTextAndRecoversOnNextChange()
    {
        var failPreview = true;
        await AuthenticateAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/html/preview")
            {
                if (failPreview)
                {
                    await route.FulfillAsync(new() { Status = 500 });
                    return;
                }

                using var request = JsonDocument.Parse(route.Request.PostData!);
                var value = request.RootElement.GetProperty("html").GetString();
                await JsonAsync(route, new { html = $"<p>{value}</p>" });
                return;
            }

            await JsonAsync(route, new { });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        var html = Page.GetByLabel("Article Html");
        await html.FillAsync("Text that must survive");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Preview is temporarily unavailable.");
        await Expect(html).ToHaveValueAsync("Text that must survive");

        failPreview = false;
        await html.FillAsync("Recovered preview");
        await Expect(Page.Locator("article.article-preview")).ToHaveTextAsync("Recovered preview");
        await Expect(Page.Locator(".preview-error")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DangerousHtml_IsSanitizedInPreviewPersistenceAndPublishedArticle()
    {
        const string dangerousHtml = """
            <p id="safe-content">Safe article content</p>
            <a id="safe-link" href="#safe-content">Safe link</a>
            <script>window.articleXssEvents.push('script')</script>
            <img id="broken-image" src="/missing-xss-image" onerror="window.articleXssEvents.push('onerror')" style="display:none">
            <a id="unsafe-link" href="javascript:window.articleXssEvents.push('javascript')" onclick="window.articleXssEvents.push('onclick')">Unsafe link</a>
            <iframe src="https://example.test/iframe"></iframe>
            <svg viewBox="0 0 20 20" onload="window.articleXssEvents.push('svg')">
              <path d="M 0 0 L 20 20" style="stroke: #000; stroke-width: 2" />
              <script>window.articleXssEvents.push('svg-script')</script>
              <foreignObject><iframe src="https://example.test/svg-frame"></iframe></foreignObject>
              <use href="javascript:window.articleXssEvents.push('svg-link')" />
            </svg>
            """;
        var uniqueId = Guid.NewGuid().ToString("N");
        var title = "Sanitized article " + uniqueId;
        var slug = "sanitized-article-" + uniqueId;
        var (login, password) = environment.GetAdminCredentials();

        // The probe is installed before each document starts, including reload and public navigation.
        await Page.AddInitScriptAsync("window.articleXssEvents = [];");
        await SignInAsync(login, password);
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());

        await Page.GetByLabel("Article title").FillAsync(title);
        await Page.Locator(".metadata-slug input").FillAsync(slug);
        await Page.GetByLabel("Article Html").FillAsync(dangerousHtml);

        // Preview must retain content but remove executable tags, attributes, styles, and URI schemes.
        var preview = Page.Locator("article.article-preview");
        await AssertSanitizedArticleAsync(preview);

        var saveButton = Page.Locator(".save-action > button");
        await Expect(saveButton).ToBeEnabledAsync();
        await saveButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/[0-9a-f-]{36}$"));
        await Expect(saveButton).ToBeDisabledAsync();
        var articleId = Guid.Parse(new Uri(Page.Url).Segments[^1].Trim('/'));

        // Reading the table avoids masking a write-path regression with read-time sanitization.
        var storedHtml = await environment.GetArticleLocalizationHtmlAsync(articleId, "en");
        Assert.Contains("Safe article content", storedHtml, StringComparison.Ordinal);
        AssertSanitizedHtml(storedHtml);

        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync(new Regex("Safe article content"));
        AssertSanitizedHtml(await Page.GetByLabel("Article Html").InputValueAsync());
        await AssertNoArticleJavaScriptExecutedAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/en/articles/{slug}").ToString());

        // Public rendering receives the persisted article through the real public API.
        var article = Page.Locator("article.article-body");
        await AssertSanitizedArticleAsync(article);
    }

    [Fact]
    public async Task MatplotlibSvg_GoldenFixtureSurvivesPreviewPersistencePublicationAndPdfExport()
    {
        await environment.EnsurePdfWorkerAsync();
        var uniqueId = Guid.NewGuid().ToString("N");
        var title = "Matplotlib SVG " + uniqueId;
        var slug = "matplotlib-svg-" + uniqueId;
        var (login, password) = environment.GetAdminCredentials();
        var source = MatplotlibSvgFixture.Get();

        await SignInAsync(login, password);
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        await Page.GetByLabel("Article title").FillAsync(title);
        await Page.Locator(".metadata-slug input").FillAsync(slug);
        await Page.GetByLabel("Article Html").FillAsync(source);

        var previewSvg = Page.Locator("article.article-preview svg");
        await Expect(previewSvg).ToHaveCountAsync(1);
        Assert.True(await previewSvg.EvaluateAsync<bool>("element => element.getBBox().width > 0"));
        Assert.True(await previewSvg.Locator("use").CountAsync() > 0);
        Assert.True(await previewSvg.Locator("clipPath").CountAsync() > 0);
        Assert.True(await previewSvg.Locator("tspan").CountAsync() > 0);

        var saveButton = Page.Locator(".save-action > button");
        await saveButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/[0-9a-f-]{36}$"));
        var articleId = Guid.Parse(new Uri(Page.Url).Segments[^1].Trim('/'));
        var storedHtml = await environment.GetArticleLocalizationHtmlAsync(articleId, "en");
        Assert.Contains("<svg", storedHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("article-svg-", storedHtml, StringComparison.Ordinal);

        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync(new Regex("<svg", RegexOptions.IgnoreCase));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");

        var queuedResponseTask = Page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("/api/admin/articles/", StringComparison.Ordinal)
            && response.Url.Contains("/pdf-exports", StringComparison.Ordinal));
        var downloadResponseTask = Page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("/api/admin/pdf-exports/", StringComparison.Ordinal)
            && response.Url.Contains("/download", StringComparison.Ordinal));
        var downloadTask = Page.WaitForDownloadAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Export PDF" }).ClickAsync();
        Assert.Equal(202, (await queuedResponseTask).Status);
        var downloadResponse = await downloadResponseTask;
        var download = await downloadTask;
        Assert.Equal(200, downloadResponse.Status);
        Assert.StartsWith("application/pdf", downloadResponse.Headers["content-type"], StringComparison.OrdinalIgnoreCase);
        Assert.Null(await download.FailureAsync());

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/en/articles/{slug}").ToString());
        var publicSvg = Page.Locator("article.article-body svg");
        await Expect(publicSvg).ToHaveCountAsync(1);
        Assert.True(await publicSvg.EvaluateAsync<bool>("element => element.getBBox().width > 0"));
    }

    [Fact]
    public async Task ExtendedHtml_IsRenderedAndTypesetInPreviewAndPublishedArticle()
    {
        const string rendered = """
            <h1>Advanced document</h1><p><a href="https://example.com">link</a></p>
            <ul><li>one</li><li>two</li></ul><table><tbody><tr><td>cell</td></tr></tbody></table>
            <pre><code class="language-csharp"><span class="keyword">return</span> 1;</code></pre>
            <span class="math">\(E=mc^2\)</span>
            """;
        await Page.AddInitScriptAsync("""
            window.MathJax = {
              typesetClear() {}, texReset() {},
              typesetPromise: async roots => roots[0].querySelectorAll('.math').forEach(source => {
                const output = document.createElement('mjx-container');
                output.textContent = 'typeset:' + source.textContent;
                source.replaceWith(output);
              })
            };
            """);
        await AuthenticateAsync();
        await RouteHtmlEditorAndPublicAsync(rendered);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        await Page.GetByLabel("Article Html").FillAsync(rendered);
        var preview = Page.Locator("article.article-preview");
        await Expect(preview.Locator("h1")).ToHaveTextAsync("Advanced document");
        await Expect(preview.Locator("ul li")).ToHaveCountAsync(2);
        await Expect(preview.Locator("table td")).ToHaveTextAsync("cell");
        await Expect(preview.Locator("code .hljs-keyword")).ToHaveTextAsync("return");
        await Expect(preview.Locator("mjx-container")).ToContainTextAsync("E=mc^2");

        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/advanced-document").ToString());
        var article = Page.Locator("article.article-body");
        await Expect(article.Locator("h1")).ToHaveTextAsync("Advanced document");
        await Expect(article.Locator("mjx-container")).ToContainTextAsync("E=mc^2");
    }

    [Fact]
    public async Task TableOfContents_StaysOutOfSourceHtmlAndUsesCurrentPageFragments()
    {
        const string source = """
            <div class="article-toc"><h2>Contents</h2></div>
            <section><h2>First topic</h2></section>
            <section id="éclair"><h2>Second topic</h2></section>
            """;
        var padding = string.Concat(Enumerable.Repeat("<p>Padding before the second section.</p>", 40));
        var rendered = $"""
            <div class="article-toc"><h2>Contents</h2><ol><li><a href="#article-section-1">First topic</a></li><li><a href="#éclair">Second topic</a></li></ol></div>
            <section id="article-section-1"><h2>First topic</h2>{padding}</section>
            <section id="éclair"><h2>Second topic</h2></section>
            """;
        await AuthenticateAsync();
        await RouteHtmlEditorAndPublicAsync(rendered);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        var html = Page.GetByLabel("Article Html");
        await html.FillAsync(source);
        await Expect(html).ToHaveValueAsync(source);
        var previewLink = Page.Locator("article.article-preview .article-toc > ol > li > a").Nth(1);
        await Expect(previewLink).ToHaveAttributeAsync("href", "/admin/articles/new#%C3%A9clair");
        await previewLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/new#%C3%A9clair$"));
        await Expect(Page.Locator("article.article-preview #éclair")).ToBeInViewportAsync();

        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/advanced-document#%C3%A9clair").ToString());
        var publicLink = Page.Locator("article.article-body .article-toc > ol > li > a").Nth(1);
        await Expect(publicLink).ToHaveAttributeAsync("href", "/en/articles/advanced-document#%C3%A9clair");
        await Expect(Page.Locator("article.article-body #éclair")).ToBeInViewportAsync();
        await publicLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/en/articles/advanced-document#%C3%A9clair$"));
        await Expect(Page.Locator("article.article-body #éclair")).ToBeInViewportAsync();
    }

    [Fact]
    public async Task PublicArticle_RemainsReadableWhenMathJaxCdnIsUnavailable()
    {
        await Page.RouteAsync("https://cdn.jsdelivr.net/**", route => route.AbortAsync("failed"));
        await Page.RouteAsync("**/api/public/articles/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.EndsWith("/views", StringComparison.Ordinal))
            {
                await JsonAsync(route, new { viewCount = 1L });
                return;
            }

            await JsonAsync(route, PublicArticle("math-fallback", "Math fallback", "<p>Readable text</p><span class=\"math\">\\(x^2\\)</span>"));
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/math-fallback").ToString());

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Math fallback" })).ToBeVisibleAsync();
        await Expect(Page.Locator("article.article-body")).ToContainTextAsync("Readable text");
        await Expect(Page.Locator("article.article-body")).ToContainTextAsync("x^2");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Article not found" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ImageInsertion_UsesSelectionSupportsSpecialNamesAndSurvivesSaveReloadAndPublication()
    {
        var articleId = Guid.NewGuid();
        var state = "Before SELECT after";
        var uploadIndex = 0;
        await AuthenticateAsync(role: "Author");
        await RouteImageEditorAsync(articleId, () => state, value => state = value, () => ++uploadIndex);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{articleId}").ToString());
        var html = Page.GetByLabel("Article Html");
        await html.EvaluateAsync("""editor => { editor.focus(); editor.setSelectionRange(7, 13); editor.dispatchEvent(new Event('select', { bubbles: true })); }""");
        await UploadAsync("diagram[one].png", "image/png", OnePixelPng);
        await Expect(html).ToHaveValueAsync(new Regex("^Before\\s+<figure class=\"article-figure\"><img src=\"/media/image-1\" alt=\"diagramone\" loading=\"lazy\"></figure>\\s+after$"));

        await html.EvaluateAsync("""editor => { editor.focus(); editor.setSelectionRange(6, 6); editor.dispatchEvent(new Event('select', { bubbles: true })); }""");
        await UploadAsync("photo.jpeg", "image/jpeg", [0xff, 0xd8, 0xff, 0xe0]);
        await Expect(html).ToHaveValueAsync(new Regex("<img src=\"/media/image-2\" alt=\"photo\" loading=\"lazy\">"));
        await UploadAsync("animation.gif", "image/gif", "GIF89a"u8.ToArray());
        await Expect(html).ToHaveValueAsync(new Regex("<img src=\"/media/image-3\" alt=\"animation\" loading=\"lazy\">"));
        await UploadAsync("drawing.webp", "image/webp", "RIFF0000WEBP"u8.ToArray());
        await Expect(html).ToHaveValueAsync(new Regex("<img src=\"/media/image-4\" alt=\"drawing\" loading=\"lazy\">"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Page.ReloadAsync();
        await Expect(html).ToHaveValueAsync(state);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/image-workflow").ToString());
        await Expect(Page.Locator("article.article-body img")).ToHaveCountAsync(4);
        await Expect(Page.Locator("article.article-body img[alt='photo']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("article.article-body img[alt='diagramone']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("article.article-body img[alt='animation']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("article.article-body img[alt='drawing']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task ImageUploadErrors_KeepHtmlAndNextValidUploadSucceeds()
    {
        var uploadAttempt = 0;
        await AuthenticateAsync(role: "Author");
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/html/preview") { await JsonAsync(route, new { html = "<p>Original</p>" }); return; }
            if (path == "/api/admin/media")
            {
                uploadAttempt++;
                if (uploadAttempt <= 2)
                {
                    var message = uploadAttempt == 1
                        ? "Only PNG, JPEG, GIF and WebP images are supported."
                        : "The file extension, content type and image contents do not match.";
                    await JsonAsync(route, new { code = "invalid_request", message }, 400);
                    return;
                }

                if (uploadAttempt == 3) { await route.AbortAsync("failed"); return; }
                await JsonAsync(route, UploadResponse("recovered.png", "image/png", "/media/recovered"), 201);
                return;
            }

            await JsonAsync(route, new { });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        var html = Page.GetByLabel("Article Html");
        await html.FillAsync("Original text");

        await UploadAsync("too-large.png", "image/png", new byte[10 * 1024 * 1024 + 1]);
        await Expect(Page.Locator(".upload-error")).ToContainTextAsync("larger than 10 MB");
        await Expect(html).ToHaveValueAsync("Original text");

        await UploadAsync("notes.txt", "text/plain", "plain text"u8.ToArray());
        await Expect(Page.Locator(".upload-error")).ToContainTextAsync("Only PNG, JPEG, GIF and WebP");
        await Expect(html).ToHaveValueAsync("Original text");

        await UploadAsync("fake.jpg", "image/jpeg", OnePixelPng);
        await Expect(Page.Locator(".upload-error")).ToContainTextAsync("do not match");
        await Expect(html).ToHaveValueAsync("Original text");

        await UploadAsync("network.png", "image/png", OnePixelPng);
        await Expect(Page.Locator(".upload-error")).ToBeVisibleAsync();
        await Expect(html).ToHaveValueAsync("Original text");

        await UploadAsync("recovered.png", "image/png", OnePixelPng);
        await Expect(Page.Locator(".upload-error")).ToHaveCountAsync(0);
        await Expect(html).ToHaveValueAsync(new Regex("Original text\\s+<figure class=\"article-figure\"><img src=\"/media/recovered\" alt=\"recovered\" loading=\"lazy\"></figure>"));
    }

    private async Task AuthenticateAsync(string role = "Admin")
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{{\"sub\":\"html-author\",\"role\":\"{role}\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Page.AddInitScriptAsync($"sessionStorage.setItem('gaifulinlab.admin.access_token','header.{payload}.signature');");
    }

    private async Task SignInAsync(string login, string password)
    {
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My articles" })).ToBeVisibleAsync();
    }

    private async Task AssertSanitizedArticleAsync(ILocator content)
    {
        await Expect(content.Locator("#safe-content")).ToHaveTextAsync("Safe article content");
        await Expect(content.Locator("#safe-link")).ToHaveAttributeAsync("href", "#safe-content");
        await Expect(content.Locator("script, iframe, foreignObject, image")).ToHaveCountAsync(0);
        var svg = content.Locator("svg");
        await Expect(svg).ToHaveCountAsync(1);
        await Expect(svg.Locator("path")).ToHaveCountAsync(1);
        Assert.True(await svg.EvaluateAsync<bool>("element => element.getBBox().width > 0"));
        await Expect(content.Locator("[onerror], [onclick], [onload], [style]")).ToHaveCountAsync(0);
        await Expect(content.Locator("[href^='javascript:']")).ToHaveCountAsync(0);
        await AssertNoArticleJavaScriptExecutedAsync();
    }

    private async Task AssertNoArticleJavaScriptExecutedAsync() =>
        Assert.Empty(await Page.EvaluateAsync<string[]>("() => window.articleXssEvents"));

    private static void AssertSanitizedHtml(string html)
    {
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<svg", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    private async Task UploadAsync(string name, string mimeType, byte[] bytes) =>
        await Page.Locator(".image-upload input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = name,
            MimeType = mimeType,
            Buffer = bytes
        });

    private async Task RouteHtmlEditorAndPublicAsync(string html)
    {
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/html/preview") { await JsonAsync(route, new { html }); return; }
            await JsonAsync(route, new { });
        });
        await Page.RouteAsync("**/api/public/articles/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.EndsWith("/views", StringComparison.Ordinal)) { await JsonAsync(route, new { viewCount = 1L }); return; }
            await JsonAsync(route, PublicArticle("advanced-document", "Advanced document", html));
        });
    }

    private async Task RouteImageEditorAsync(
        Guid articleId,
        Func<string> getHtml,
        Action<string> setHtml,
        Func<int> nextUpload)
    {
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/html/preview") { await JsonAsync(route, new { html = HtmlImagesToHtml(getHtml()) }); return; }
            if (path == "/api/admin/media")
            {
                var index = nextUpload();
                var (fileName, contentType) = index switch
                {
                    1 => ("diagram[one].png", "image/png"),
                    2 => ("photo.jpeg", "image/jpeg"),
                    3 => ("animation.gif", "image/gif"),
                    _ => ("drawing.webp", "image/webp")
                };
                await JsonAsync(route, UploadResponse(fileName, contentType, $"/media/image-{index}"), 201);
                return;
            }
            if (path == $"/api/admin/articles/{articleId}" && route.Request.Method == "GET")
            {
                await JsonAsync(route, ArticleDetails(articleId, getHtml()));
                return;
            }
            if (path == $"/api/admin/articles/{articleId}/localizations/en" && route.Request.Method == "PUT")
            {
                using var request = JsonDocument.Parse(route.Request.PostData!);
                setHtml(request.RootElement.GetProperty("html").GetString() ?? "");
                await JsonAsync(route, 2L);
                return;
            }
            await JsonAsync(route, new { });
        });
        await Page.RouteAsync("**/api/public/articles/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.EndsWith("/views", StringComparison.Ordinal)) { await JsonAsync(route, new { viewCount = 1L }); return; }
            await JsonAsync(route, PublicArticle("image-workflow", "Image workflow", HtmlImagesToHtml(getHtml())));
        });
    }

    private static object ArticleDetails(Guid articleId, string html) => new
    {
        id = articleId,
        createdAt = DateTimeOffset.UtcNow.AddDays(-1),
        updatedAt = DateTimeOffset.UtcNow,
        localizations = new[] { Localization("en", "image-workflow", "Image workflow", html) },
        topicIds = Array.Empty<Guid>(),
        series = Array.Empty<object>(),
        tags = Array.Empty<string>()
    };

    private static object ArticleDetails(Guid articleId, Guid topicId, Guid fallbackTopicId, Guid seriesId) => new
    {
        id = articleId,
        createdAt = DateTimeOffset.UtcNow.AddDays(-1),
        updatedAt = DateTimeOffset.UtcNow,
        localizations = new[]
        {
            Localization("en", "taxonomy-en", "Taxonomy EN", "Body"),
            Localization("ru", "taxonomy-ru", "Taxonomy RU", "Текст")
        },
        topicIds = new[] { topicId, fallbackTopicId },
        series = new[] { new { seriesId, position = 1 } },
        tags = Array.Empty<string>()
    };

    private static object Localization(string language, string slug, string title, string html) => new
    {
        id = Guid.NewGuid(),
        version = 1L,
        languageCode = language,
        slug,
        title,
        summary = "Summary",
        html,
        status = 0,
        publishedAt = (DateTimeOffset?)null,
        updatedAt = DateTimeOffset.UtcNow,
        lastEditedAt = DateTimeOffset.UtcNow
    };

    private static object TaxonomyTopic(Guid id, params (string Language, string Name)[] names) => new
    {
        id,
        createdAt = DateTimeOffset.UtcNow.AddDays(-1),
        updatedAt = DateTimeOffset.UtcNow,
        localizations = names.Select(name => new
        {
            id = Guid.NewGuid(), languageCode = name.Language, name = name.Name,
            slug = name.Name.ToLowerInvariant().Replace(' ', '-'), description = ""
        }).ToArray()
    };

    private static object TaxonomySeries(Guid id, params (string Language, string Name)[] names) => new
    {
        id,
        createdAt = DateTimeOffset.UtcNow.AddDays(-1),
        updatedAt = DateTimeOffset.UtcNow,
        localizations = names.Select(name => new
        {
            id = Guid.NewGuid(), languageCode = name.Language, title = name.Name,
            slug = name.Name.ToLowerInvariant().Replace(' ', '-'), description = ""
        }).ToArray(),
        articles = Array.Empty<object>()
    };

    private static object UploadResponse(string name, string contentType, string url) => new
    {
        id = Guid.NewGuid(), url, originalFileName = name, contentType, size = 42L,
        width = (int?)null, height = (int?)null
    };

    private static object PublicArticle(string slug, string title, string html) => new
    {
        languageCode = "en", slug, title, summary = "Summary", html,
        publishedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        updatedAt = DateTimeOffset.UtcNow,
        lastEditedAt = DateTimeOffset.UtcNow,
        availableLocalizations = new[] { new { languageCode = "en", url = $"/en/articles/{slug}" } },
        topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<string>(), viewCount = 0L
    };

    private static string HtmlImagesToHtml(string html) => html;

    private static object EmptyTaxonomy() => new
    {
        topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<object>()
    };

    private static Task JsonAsync(IRoute route, object body, int status = 200) =>
        route.FulfillAsync(new()
        {
            Status = status,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, body.GetType())
        });
}
