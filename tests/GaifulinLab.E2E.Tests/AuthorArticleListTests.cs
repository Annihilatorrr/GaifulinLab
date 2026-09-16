using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class AuthorArticleListTests(E2EEnvironment environment) : E2EPageTest
{
    [Fact]
    public async Task EmptyWorkspace_CreatesASavedDraftThatAppearsInTheAuthorList()
    {
        var articleId = Guid.NewGuid();
        var localizationId = Guid.NewGuid();
        var stored = false;
        const string title = "First saved draft";
        const string html = "The draft body is saved.";
        await AuthenticateAsAuthorAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            object body = path switch
            {
                "/api/admin/taxonomy" => new { topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<string>() },
                "/api/admin/html/preview" => new { html = "<p>Preview</p>" },
                "/api/admin/articles" when route.Request.Method == "GET" => stored
                    ? Paged(new[] { ListItem(articleId, title, "en", 0, DateTimeOffset.UtcNow) }, 1)
                    : Paged(Array.Empty<object>(), 0),
                "/api/admin/articles" when route.Request.Method == "POST" => new { articleId, localizationId, localizationVersion = 0L },
                _ when path == $"/api/admin/articles/{articleId}" => ArticleDetails(articleId, localizationId, title, html),
                _ => new { }
            };
            if (path == "/api/admin/articles" && route.Request.Method == "POST") stored = true;
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = JsonSerializer.Serialize(body) });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "No articles yet" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Create first article" }).ClickAsync();
        await Page.GetByLabel("Article title").FillAsync(title);
        await Page.GetByLabel("Article Html").FillAsync(html);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/admin/articles/{articleId}$"));
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync(title);
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync(html);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Back to articles" }).ClickAsync();
        await Expect(Page.GetByText(title, new() { Exact = true })).ToBeVisibleAsync();
        await Page.GetByText(title, new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync(html);
    }

    [Fact]
    public async Task AuthorList_UsesSelectedLanguageAndKeepsFallbackArticlesInApiOrder()
    {
        var newest = Guid.NewGuid();
        var fallback = Guid.NewGuid();
        var older = Guid.NewGuid();
        await AuthenticateAsAuthorAsync();
        await Page.AddInitScriptAsync(
            "if (!localStorage.getItem('GaifulinLab.Web.UiCulture')) "
            + "localStorage.setItem('GaifulinLab.Web.UiCulture', 'ru');");
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
                new
                {
                    id = newest, createdAt = now.AddDays(-2), updatedAt = now,
                    localizations = new[]
                    {
                        new { id = Guid.NewGuid(), languageCode = "en", slug = "newest-en", title = "Newest English", status = 2, publishedAt = (DateTimeOffset?)null, updatedAt = now, lastEditedAt = now },
                        new { id = Guid.NewGuid(), languageCode = "RU", slug = "newest-ru", title = "Самая новая русская", status = 1, publishedAt = (DateTimeOffset?)now.AddDays(-1), updatedAt = now, lastEditedAt = now }
                    }
                },
                ListItem(fallback, "English fallback", "en", 0, now.AddMinutes(-1)),
                ListItem(older, "", "ru", 0, now.AddMinutes(-2))
            };
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = JsonSerializer.Serialize(Paged(items, 3)) });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles").ToString());
        var rows = Page.Locator(".article-row");
        await Expect(rows).ToHaveCountAsync(3);
        var rowText = await rows.AllTextContentsAsync();

        // Russian is selected case-insensitively and moves its metadata ahead of the English localization.
        Assert.Contains("Самая новая русская", rowText[0]);
        Assert.DoesNotContain("Newest English", rowText[0]);
        Assert.Contains("RU — Опубликовано · EN — Снято с публикации", rowText[0]);

        // Articles without a Russian localization remain visible, and API article ordering is unchanged.
        Assert.Contains("English fallback", rowText[1]);
        Assert.Contains("Статья без названия", rowText[2]);
        Assert.Contains("RU — Черновик", rowText[2]);

        // Switching the UI language reloads the same route and selects the matching article localization.
        await Page.GetByTestId("language-toggle").ClickAsync();
        await Page.GetByTestId("language-en").ClickAsync();
        await Expect(Page.GetByTestId("language-current")).ToHaveTextAsync("EN");
        rowText = await rows.AllTextContentsAsync();
        Assert.Contains("Newest English", rowText[0]);
        Assert.DoesNotContain("Самая новая русская", rowText[0]);
        Assert.Contains("EN — Unpublished · RU — Published", rowText[0]);
        Assert.Contains("English fallback", rowText[1]);
        Assert.Contains("Untitled article", rowText[2]);
    }

    [Fact]
    public async Task AuthorList_PersistsPagedNavigationAndPageSizeInTheUrl()
    {
        var now = DateTimeOffset.UtcNow;
        var articles = Enumerable.Range(1, 23)
            .Select(index => ListItem(Guid.NewGuid(), $"Article {index:00}", "en", 0, now.AddMinutes(-index)))
            .ToArray();
        var requestedUrls = new List<string>();
        await AuthenticateAsAuthorAsync();
        await Page.RouteAsync("**/api/admin/articles**", async route =>
        {
            if (route.Request.Method != "GET")
            {
                await route.FallbackAsync();
                return;
            }

            requestedUrls.Add(route.Request.Url);
            var uri = new Uri(route.Request.Url);
            var page = QueryValue(uri, "page", 1);
            var pageSize = QueryValue(uri, "pageSize", 10);
            var items = articles.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
            await JsonAsync(route, Paged(items, articles.Length, page, pageSize));
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles?page=2&pageSize=10").ToString());
        await Expect(Page.Locator(".article-row")).ToHaveCountAsync(10);
        await Expect(Page.GetByText("Article 11", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator(".page-summary")).ToHaveTextAsync("Page 2 of 3");
        Assert.Contains(requestedUrls, url => url.Contains("page=2&pageSize=10", StringComparison.Ordinal));

        await Page.ReloadAsync();
        await Expect(Page.GetByText("Article 11", new() { Exact = true })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Page 3", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".article-row")).ToHaveCountAsync(3);
        Assert.Contains("page=3&pageSize=10", Page.Url, StringComparison.Ordinal);

        await Page.GoBackAsync();
        await Expect(Page.GetByText("Article 11", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Contains("page=2&pageSize=10", Page.Url, StringComparison.Ordinal);

        await Page.GetByLabel("Items per page", new() { Exact = true }).SelectOptionAsync("20");
        await Expect(Page.Locator(".article-row")).ToHaveCountAsync(20);
        Assert.Contains("page=1&pageSize=20", Page.Url, StringComparison.Ordinal);
        await Page.GoBackAsync();
        await Expect(Page.GetByText("Article 11", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Contains("page=2&pageSize=10", Page.Url, StringComparison.Ordinal);
        await Page.GoForwardAsync();
        await Expect(Page.Locator(".article-row")).ToHaveCountAsync(20);
        Assert.Contains("page=1&pageSize=20", Page.Url, StringComparison.Ordinal);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles?page=invalid&pageSize=invalid").ToString());
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/admin/articles\\?page=1&pageSize=10$"));
        await Expect(Page.Locator(".article-row")).ToHaveCountAsync(10);
    }

    [Fact]
    public async Task AuthorList_DeletingTheOnlyItemOnTheLastPageMovesToThePreviousPage()
    {
        var now = DateTimeOffset.UtcNow;
        var articles = Enumerable.Range(1, 21)
            .Select(index => ListItem(Guid.NewGuid(), $"Delete page article {index:00}", "en", 0, now.AddMinutes(-index)))
            .ToList();
        await AuthenticateAsAuthorAsync();
        await Page.RouteAsync("**/api/admin/articles**", async route =>
        {
            var uri = new Uri(route.Request.Url);
            if (route.Request.Method == "DELETE")
            {
                var id = Guid.Parse(uri.AbsolutePath.Split('/').Last());
                articles.RemoveAll(item => GetId(item) == id);
                await route.FulfillAsync(new() { Status = 204 });
                return;
            }

            var requestedPage = QueryValue(uri, "page", 1);
            var pageSize = QueryValue(uri, "pageSize", 10);
            var totalPages = articles.Count == 0 ? 0 : (int)Math.Ceiling(articles.Count / (double)pageSize);
            var page = totalPages == 0 ? 1 : Math.Min(requestedPage, totalPages);
            var items = articles.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
            await JsonAsync(route, Paged(items, articles.Count, page, pageSize));
        });
        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles?page=3&pageSize=10").ToString());
        await Expect(Page.Locator(".article-row")).ToHaveCountAsync(1);
        await Page.Locator(".article-row").GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/admin/articles\\?page=2&pageSize=10$"));
        await Expect(Page.Locator(".article-row")).ToHaveCountAsync(10);
        await Expect(Page.Locator(".page-summary")).ToHaveTextAsync("Page 2 of 2");
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

    private static object Paged(object items, int totalCount, int page = 1, int pageSize = 10) => new
    {
        items,
        totalCount,
        page,
        pageSize,
        totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
    };

    private static int QueryValue(Uri uri, string name, int fallback)
    {
        var value = uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(part => string.Equals(part[0], name, StringComparison.OrdinalIgnoreCase));
        return value is { Length: 2 } && int.TryParse(value[1], out var parsed) ? parsed : fallback;
    }

    private static Guid GetId(object item) =>
        (Guid)item.GetType().GetProperty("id")!.GetValue(item)!;

    private static Task JsonAsync(IRoute route, object body, int status = 200) =>
        route.FulfillAsync(new()
        {
            Status = status,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, body.GetType())
        });

    private static object ArticleDetails(Guid articleId, Guid localizationId, string title, string html) => new
    {
        id = articleId, createdAt = DateTimeOffset.UtcNow.AddHours(-1), updatedAt = DateTimeOffset.UtcNow,
        localizations = new[]
        {
            new { id = localizationId, version = 0L, languageCode = "en", slug = "first-saved-draft", title, summary = (string?)null, html, status = 0, publishedAt = (DateTimeOffset?)null, updatedAt = DateTimeOffset.UtcNow, lastEditedAt = DateTimeOffset.UtcNow, coverMediaAssetId = (Guid?)null }
        },
        topicIds = Array.Empty<Guid>(), series = Array.Empty<object>(), tags = Array.Empty<string>()
    };
}
