using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class AuthorArticleListTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task EmptyWorkspace_CreatesASavedDraftThatAppearsInTheAuthorList()
    {
        var articleId = Guid.NewGuid();
        var localizationId = Guid.NewGuid();
        var stored = false;
        const string title = "First saved draft";
        const string markdown = "The draft body is saved.";
        await AuthenticateAsAuthorAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            object body = path switch
            {
                "/api/admin/taxonomy" => new { topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<string>() },
                "/api/admin/markdown/preview" => new { html = "<p>Preview</p>" },
                "/api/admin/articles" when route.Request.Method == "GET" => stored
                    ? new[] { ListItem(articleId, title, "en", 0, DateTimeOffset.UtcNow) }
                    : Array.Empty<object>(),
                "/api/admin/articles" when route.Request.Method == "POST" => new { articleId, localizationId, localizationVersion = 0L },
                _ when path == $"/api/admin/articles/{articleId}" => ArticleDetails(articleId, localizationId, title, markdown),
                _ => new { }
            };
            if (path == "/api/admin/articles" && route.Request.Method == "POST") stored = true;
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = JsonSerializer.Serialize(body) });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "No articles yet" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Create first article" }).ClickAsync();
        await Page.GetByLabel("Article title").FillAsync(title);
        await Page.GetByLabel("Article Markdown").FillAsync(markdown);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/admin/articles/{articleId}$"));
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync(title);
        await Expect(Page.GetByLabel("Article Markdown")).ToHaveValueAsync(markdown);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Back to articles" }).ClickAsync();
        await Expect(Page.GetByText(title, new() { Exact = true })).ToBeVisibleAsync();
        await Page.GetByText(title, new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByLabel("Article Markdown")).ToHaveValueAsync(markdown);
    }

    [Fact]
    public async Task AuthorList_UsesFallbackTitlesStatusesLanguagesAndApiOrder()
    {
        var newest = Guid.NewGuid();
        var untitled = Guid.NewGuid();
        var older = Guid.NewGuid();
        await AuthenticateAsAuthorAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            if (new Uri(route.Request.Url).AbsolutePath != "/api/admin/articles")
            {
                await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" });
                return;
            }
            var now = DateTimeOffset.UtcNow;
            var items = new[]
            {
                ListItem(newest, "Changed most recently", "en", 1, now),
                ListItem(untitled, "", "ru", 0, now.AddMinutes(-1)),
                new
                {
                    id = older, createdAt = now.AddDays(-2), updatedAt = now.AddMinutes(-2),
                    localizations = new[]
                    {
                        new { id = Guid.NewGuid(), languageCode = "en", slug = "older-en", title = "Older English", status = 2, publishedAt = (DateTimeOffset?)null, updatedAt = now.AddMinutes(-2), lastEditedAt = now.AddMinutes(-2) },
                        new { id = Guid.NewGuid(), languageCode = "ru", slug = "older-ru", title = "Older Russian", status = 1, publishedAt = (DateTimeOffset?)now.AddDays(-1), updatedAt = now.AddMinutes(-2), lastEditedAt = now.AddMinutes(-2) }
                    }
                }
            };
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = JsonSerializer.Serialize(items) });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles").ToString());
        var rows = Page.Locator(".article-row");
        await Expect(rows).ToHaveCountAsync(3);
        var rowText = await rows.AllTextContentsAsync();
        Assert.Contains("Changed most recently", rowText[0]);
        Assert.Contains("Untitled article", rowText[1]);
        Assert.Contains("RU — Draft", rowText[1]);
        Assert.Contains("Older English", rowText[2]);
        Assert.Contains("EN — Unpublished", rowText[2]);
        Assert.Contains("RU — Published", rowText[2]);
    }

    private async Task AuthenticateAsAuthorAsync()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            sub = "mock-author", unique_name = "mock-author", role = "Admin",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        }))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Page.AddInitScriptAsync($"sessionStorage.setItem('gaifulinlab.admin.access_token', 'header.{payload}.signature');");
    }

    private static object ListItem(Guid id, string title, string languageCode, int status, DateTimeOffset updatedAt) => new
    {
        id, createdAt = updatedAt.AddHours(-1), updatedAt,
        localizations = new[]
        {
            new { id = Guid.NewGuid(), languageCode, slug = "first-saved-draft", title, status, publishedAt = (DateTimeOffset?)null, updatedAt, lastEditedAt = updatedAt }
        }
    };

    private static object ArticleDetails(Guid articleId, Guid localizationId, string title, string markdown) => new
    {
        id = articleId, createdAt = DateTimeOffset.UtcNow.AddHours(-1), updatedAt = DateTimeOffset.UtcNow,
        localizations = new[]
        {
            new { id = localizationId, version = 0L, languageCode = "en", slug = "first-saved-draft", title, summary = (string?)null, markdown, status = 0, publishedAt = (DateTimeOffset?)null, updatedAt = DateTimeOffset.UtcNow, lastEditedAt = DateTimeOffset.UtcNow, coverMediaAssetId = (Guid?)null }
        },
        topicIds = Array.Empty<Guid>(), series = Array.Empty<object>(), tags = Array.Empty<string>()
    };
}
