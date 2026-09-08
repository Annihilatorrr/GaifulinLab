using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleMarkdownAndMediaTests(E2EEnvironment environment) : PageTest
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

            await JsonAsync(route, path == "/api/admin/markdown/preview"
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

            if (path == "/api/admin/markdown/preview")
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
    public async Task Preview_IgnoresLateResponseAndClearingMarkdownShowsEmptyState()
    {
        var firstRequestArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await AuthenticateAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/markdown/preview")
            {
                using var request = JsonDocument.Parse(route.Request.PostData!);
                var markdown = request.RootElement.GetProperty("markdown").GetString()!;
                if (markdown == "First response")
                {
                    firstRequestArrived.TrySetResult();
                    await releaseFirst.Task;
                }

                await JsonAsync(route, new { html = $"<p>{markdown}</p>" });
                return;
            }

            await JsonAsync(route, new { });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        var markdown = Page.GetByLabel("Article Markdown");
        await markdown.FillAsync("First response");
        await firstRequestArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await markdown.FillAsync("Current response");
        await Expect(Page.Locator("article.article-preview")).ToHaveTextAsync("Current response");
        releaseFirst.TrySetResult();
        await Page.WaitForTimeoutAsync(250);
        await Expect(Page.Locator("article.article-preview")).ToHaveTextAsync("Current response");

        await markdown.FillAsync("");
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
            if (path == "/api/admin/markdown/preview")
            {
                if (failPreview)
                {
                    await route.FulfillAsync(new() { Status = 500 });
                    return;
                }

                using var request = JsonDocument.Parse(route.Request.PostData!);
                var value = request.RootElement.GetProperty("markdown").GetString();
                await JsonAsync(route, new { html = $"<p>{value}</p>" });
                return;
            }

            await JsonAsync(route, new { });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        var markdown = Page.GetByLabel("Article Markdown");
        await markdown.FillAsync("Text that must survive");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Preview is temporarily unavailable.");
        await Expect(markdown).ToHaveValueAsync("Text that must survive");

        failPreview = false;
        await markdown.FillAsync("Recovered preview");
        await Expect(Page.Locator("article.article-preview")).ToHaveTextAsync("Recovered preview");
        await Expect(Page.Locator(".preview-error")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ExtendedMarkdown_IsRenderedAndTypesetInPreviewAndPublishedArticle()
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
        await RouteMarkdownEditorAndPublicAsync(rendered);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        await Page.GetByLabel("Article Markdown").FillAsync("# Advanced document\n\n$E=mc^2$");
        var preview = Page.Locator("article.article-preview");
        await Expect(preview.Locator("h1")).ToHaveTextAsync("Advanced document");
        await Expect(preview.Locator("ul li")).ToHaveCountAsync(2);
        await Expect(preview.Locator("table td")).ToHaveTextAsync("cell");
        await Expect(preview.Locator("code .keyword")).ToHaveTextAsync("return");
        await Expect(preview.Locator("mjx-container")).ToContainTextAsync("E=mc^2");

        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/advanced-document").ToString());
        var article = Page.Locator("article.article-body");
        await Expect(article.Locator("h1")).ToHaveTextAsync("Advanced document");
        await Expect(article.Locator("mjx-container")).ToContainTextAsync("E=mc^2");
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
        var markdown = Page.GetByLabel("Article Markdown");
        await markdown.EvaluateAsync("""editor => { editor.focus(); editor.setSelectionRange(7, 13); editor.dispatchEvent(new Event('select', { bubbles: true })); }""");
        await UploadAsync("diagram[one].png", "image/png", OnePixelPng);
        await Expect(markdown).ToHaveValueAsync(new Regex("^Before\\s+!\\[diagramone\\]\\(/media/image-1\\)\\s+after$"));

        await markdown.EvaluateAsync("""editor => { editor.focus(); editor.setSelectionRange(6, 6); editor.dispatchEvent(new Event('select', { bubbles: true })); }""");
        await UploadAsync("photo.jpeg", "image/jpeg", [0xff, 0xd8, 0xff, 0xe0]);
        await Expect(markdown).ToHaveValueAsync(new Regex("!\\[photo\\]\\(/media/image-2\\)"));
        await UploadAsync("animation.gif", "image/gif", "GIF89a"u8.ToArray());
        await Expect(markdown).ToHaveValueAsync(new Regex("!\\[animation\\]\\(/media/image-3\\)"));
        await UploadAsync("drawing.webp", "image/webp", "RIFF0000WEBP"u8.ToArray());
        await Expect(markdown).ToHaveValueAsync(new Regex("!\\[drawing\\]\\(/media/image-4\\)"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Page.ReloadAsync();
        await Expect(markdown).ToHaveValueAsync(state);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/image-workflow").ToString());
        await Expect(Page.Locator("article.article-body img")).ToHaveCountAsync(4);
        await Expect(Page.Locator("article.article-body img[alt='photo']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("article.article-body img[alt='diagramone']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("article.article-body img[alt='animation']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("article.article-body img[alt='drawing']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task ImageUploadErrors_KeepMarkdownAndNextValidUploadSucceeds()
    {
        var uploadAttempt = 0;
        await AuthenticateAsync(role: "Author");
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/markdown/preview") { await JsonAsync(route, new { html = "<p>Original</p>" }); return; }
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
        var markdown = Page.GetByLabel("Article Markdown");
        await markdown.FillAsync("Original text");

        await UploadAsync("too-large.png", "image/png", new byte[10 * 1024 * 1024 + 1]);
        await Expect(Page.Locator(".upload-error")).ToContainTextAsync("larger than 10 MB");
        await Expect(markdown).ToHaveValueAsync("Original text");

        await UploadAsync("notes.txt", "text/plain", "plain text"u8.ToArray());
        await Expect(Page.Locator(".upload-error")).ToContainTextAsync("Only PNG, JPEG, GIF and WebP");
        await Expect(markdown).ToHaveValueAsync("Original text");

        await UploadAsync("fake.jpg", "image/jpeg", OnePixelPng);
        await Expect(Page.Locator(".upload-error")).ToContainTextAsync("do not match");
        await Expect(markdown).ToHaveValueAsync("Original text");

        await UploadAsync("network.png", "image/png", OnePixelPng);
        await Expect(Page.Locator(".upload-error")).ToBeVisibleAsync();
        await Expect(markdown).ToHaveValueAsync("Original text");

        await UploadAsync("recovered.png", "image/png", OnePixelPng);
        await Expect(Page.Locator(".upload-error")).ToHaveCountAsync(0);
        await Expect(markdown).ToHaveValueAsync(new Regex("Original text\\s+!\\[recovered\\]\\(/media/recovered\\)"));
    }

    private async Task AuthenticateAsync(string role = "Admin")
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{{\"sub\":\"markdown-author\",\"role\":\"{role}\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Page.AddInitScriptAsync($"sessionStorage.setItem('gaifulinlab.admin.access_token','header.{payload}.signature');");
    }

    private async Task UploadAsync(string name, string mimeType, byte[] bytes) =>
        await Page.Locator(".image-upload input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = name,
            MimeType = mimeType,
            Buffer = bytes
        });

    private async Task RouteMarkdownEditorAndPublicAsync(string html)
    {
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/markdown/preview") { await JsonAsync(route, new { html }); return; }
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
        Func<string> getMarkdown,
        Action<string> setMarkdown,
        Func<int> nextUpload)
    {
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/markdown/preview") { await JsonAsync(route, new { html = MarkdownImagesToHtml(getMarkdown()) }); return; }
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
                await JsonAsync(route, ArticleDetails(articleId, getMarkdown()));
                return;
            }
            if (path == $"/api/admin/articles/{articleId}/localizations/en" && route.Request.Method == "PUT")
            {
                using var request = JsonDocument.Parse(route.Request.PostData!);
                setMarkdown(request.RootElement.GetProperty("markdown").GetString() ?? "");
                await JsonAsync(route, 2L);
                return;
            }
            await JsonAsync(route, new { });
        });
        await Page.RouteAsync("**/api/public/articles/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.EndsWith("/views", StringComparison.Ordinal)) { await JsonAsync(route, new { viewCount = 1L }); return; }
            await JsonAsync(route, PublicArticle("image-workflow", "Image workflow", MarkdownImagesToHtml(getMarkdown())));
        });
    }

    private static object ArticleDetails(Guid articleId, string markdown) => new
    {
        id = articleId,
        createdAt = DateTimeOffset.UtcNow.AddDays(-1),
        updatedAt = DateTimeOffset.UtcNow,
        localizations = new[] { Localization("en", "image-workflow", "Image workflow", markdown) },
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

    private static object Localization(string language, string slug, string title, string markdown) => new
    {
        id = Guid.NewGuid(),
        version = 1L,
        languageCode = language,
        slug,
        title,
        summary = "Summary",
        markdown,
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
        authorDisplayName = "Test Author",
        availableLocalizations = new[] { new { languageCode = "en", url = $"/en/articles/{slug}" } },
        topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<string>(), viewCount = 0L
    };

    private static string MarkdownImagesToHtml(string markdown)
    {
        var matches = Regex.Matches(markdown, @"!\[(?<alt>[^]]*)\]\((?<url>[^)]+)\)");
        return string.Concat(matches.Select(match =>
            $"<img alt=\"{match.Groups["alt"].Value}\" src=\"{match.Groups["url"].Value}\">"));
    }

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
