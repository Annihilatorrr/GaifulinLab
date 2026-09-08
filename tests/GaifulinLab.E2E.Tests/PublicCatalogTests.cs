using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class PublicCatalogTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task ArticlesAndArchive_ShowOnlyPublishedItemsInOrderAndOpenTheSelectedArticle()
    {
        var published = Articles("Newest published", "Middle published", "Oldest published");
        await RoutePublicApiAsync((url, _) =>
            url.Contains("/articles/en/newest-published/views", StringComparison.Ordinal)
                ? "{\"viewCount\":1}"
                : url.Contains("/articles/en/newest-published", StringComparison.Ordinal)
                ? ArticleDetails("Newest published", "newest-published")
                : JsonSerializer.Serialize(published));

        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles").ToString());
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(3);
        Assert.Equal(["Newest published", "Middle published", "Oldest published"], await TitlesAsync());
        await Expect(Page.Locator(".content-byline").First).ToContainTextAsync("By Catalog author");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Newest published" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Newest published" })).ToBeVisibleAsync();

        await Page.GotoAsync(new Uri(environment.BaseUri, "/archive").ToString());
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(3);
        Assert.Equal(["Newest published", "Middle published", "Oldest published"], await TitlesAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(40)]
    [InlineData(41)]
    public async Task LoadMore_PaginatesWithoutGapsDuplicatesOrOrderChanges(int total)
    {
        var all = Articles(Enumerable.Range(1, total).Select(index => $"Article {total - index + 1:00}").ToArray());
        await RoutePublicApiAsync((_, query) => JsonSerializer.Serialize(ArticlePage(all, query)));
        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles").ToString());
        if (total > 0)
        {
            await Expect(Page.Locator(".content-item")).ToHaveCountAsync(Math.Min(total, 20));
        }

        var loaded = Math.Min(total, 20);
        while (await Page.GetByRole(AriaRole.Button, new() { Name = "Load more" }).CountAsync() > 0)
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Load more" }).ClickAsync();
            if (loaded < total)
            {
                loaded = Math.Min(total, loaded + 20);
                await Expect(Page.Locator(".content-item")).ToHaveCountAsync(loaded);
            }
            else
            {
                await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Load more" })).ToHaveCountAsync(0);
            }
        }

        var titles = await TitlesAsync();
        Assert.Equal(total, titles.Count);
        Assert.Equal(total, titles.Distinct().Count());
        Assert.Equal(all.Select(item => item.Title), titles);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Load more" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task LoadMore_DisablesDuringRequestAndAppendsTheNextPageOnlyOnce()
    {
        var all = Articles(Enumerable.Range(1, 21).Select(index => $"Delayed article {index:00}").ToArray());
        var nextPageStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNextPage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync("**/api/public/articles?**", async route =>
        {
            var query = Query(route.Request.Url);
            if (query.GetValueOrDefault("page") == "2")
            {
                nextPageStarted.TrySetResult();
                await releaseNextPage.Task;
            }

            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = JsonSerializer.Serialize(ArticlePage(all, query)) });
        });
        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles").ToString());
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(20);
        var button = Page.Locator("button.button-secondary");
        await button.ClickAsync();
        await nextPageStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Expect(button).ToBeDisabledAsync();
        releaseNextPage.TrySetResult();
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(21);
        Assert.Equal(21, (await TitlesAsync()).Distinct().Count());
    }

    [Fact]
    public async Task CatalogFiltersAndTaxonomyLinks_SelectOnlyMatchingItemsIncludingEncodedTags()
    {
        var all = Articles("Topic match", "Tag match", "Series match", "Outside");
        var encodedTag = "C# & .NET";
        await RoutePublicApiAsync((url, query) =>
        {
            if (url.Contains("/articles/en/series-match/views", StringComparison.Ordinal))
                return "{\"viewCount\":1}";
            if (url.Contains("/articles/en/series-match", StringComparison.Ordinal))
                return ArticleDetails("Series match", "series-match");
            if (url.Contains("/series/en/series-a", StringComparison.Ordinal))
                return JsonSerializer.Serialize(new
                {
                    languageCode = "en", slug = "series-a", title = "Series A", description = "",
                    articles = new[] { new { position = 1, slug = "series-match", title = "Series match", summary = "", authorDisplayName = "Catalog author" } }
                });
            if (url.Contains("/topics/", StringComparison.Ordinal))
                return JsonSerializer.Serialize(new[] { new { languageCode = "en", slug = "topic-a", name = "Topic A", description = "", articleCount = 2 } });
            if (url.Contains("/tags/", StringComparison.Ordinal))
                return JsonSerializer.Serialize(new[] { new { name = encodedTag, articleCount = 2 } });
            if (url.Contains("/series", StringComparison.Ordinal))
                return JsonSerializer.Serialize(new[] { new { languageCode = "en", slug = "series-a", title = "Series A", description = "", articleCount = 2 } });
            var selected = query.GetValueOrDefault("topic") == "topic-a" && query.GetValueOrDefault("tag") == encodedTag
                ? all.Where(item => item.Title == "Tag match")
                : query.GetValueOrDefault("topic") == "topic-a" ? all.Where(item => item.Title is "Topic match" or "Tag match")
                : query.GetValueOrDefault("tag") == encodedTag ? all.Where(item => item.Title is "Tag match" or "Series match")
                : query.GetValueOrDefault("series") == "series-a" ? all.Where(item => item.Title is "Series match" or "Tag match")
                : query.ContainsKey("topic") || query.ContainsKey("tag") || query.ContainsKey("series") ? Array.Empty<CatalogArticle>() : all;
            return JsonSerializer.Serialize(ArticlePage(selected.ToArray(), query));
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles?topic=topic-a&tag=" + Uri.EscapeDataString(encodedTag)).ToString());
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(1);
        Assert.Equal(["Tag match"], await TitlesAsync());
        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles?series=series-a").ToString());
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(2);
        Assert.Equal(["Tag match", "Series match"], await TitlesAsync());
        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles?topic=missing").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "No articles yet" })).ToBeVisibleAsync();

        await Page.GotoAsync(new Uri(environment.BaseUri, "/tags").ToString());
        await Expect(Page.Locator(".tag-list a")).ToContainTextAsync("2");
        await Page.Locator(".tag-list a").Filter(new() { HasText = "C#" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("tag=C%23%20%26%20.NET"));
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(2);
        Assert.Equal(["Tag match", "Series match"], await TitlesAsync());

        await Page.GotoAsync(new Uri(environment.BaseUri, "/topics").ToString());
        await Expect(Page.Locator(".taxonomy-card")).ToContainTextAsync("2 article(s)");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Topic A" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("topic=topic-a"));
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(2);
        Assert.Equal(["Topic match", "Tag match"], await TitlesAsync());

        await Page.GotoAsync(new Uri(environment.BaseUri, "/series").ToString());
        await Expect(Page.Locator(".taxonomy-card")).ToContainTextAsync("2 article(s)");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Series A" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Series A" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Series match" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Series match" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task FilteredLoadMoreAndBrowserHistory_KeepOnlyTheCurrentFilterResults()
    {
        var matching = Articles(Enumerable.Range(1, 23).Select(index => $"Selected {index:00}").ToArray());
        await RoutePublicApiAsync((_, query) =>
        {
            var selected = query.GetValueOrDefault("tag") == "selected"
                ? matching
                : query.GetValueOrDefault("topic") == "other" ? Articles("Other result") : Array.Empty<CatalogArticle>();
            return JsonSerializer.Serialize(ArticlePage(selected, query));
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles?tag=selected").ToString());
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(20);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Load more" }).ClickAsync();
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(23);
        Assert.All(await TitlesAsync(), title => Assert.StartsWith("Selected ", title));

        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles?topic=other").ToString());
        await Expect(Page.GetByText("Other result", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Selected 01", new() { Exact = true })).ToHaveCountAsync(0);
        await Page.GoBackAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("tag=selected"));
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(20);
        await Page.GoForwardAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("topic=other"));
        await Expect(Page.GetByText("Other result", new() { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ChangingFiltersCancelsOldPagesAndEmptyPublicPagesHaveNoStaleCards()
    {
        var delayedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RoutePublicApiAsync(async (url, query) =>
        {
            if (query.GetValueOrDefault("topic") == "a")
            {
                delayedStarted.TrySetResult();
                await releaseDelayed.Task;
                return JsonSerializer.Serialize(Articles("From filter A"));
            }

            return JsonSerializer.Serialize(query.GetValueOrDefault("topic") == "b" ? Articles("From filter B") : Array.Empty<object>());
        });
        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles?topic=a").ToString(), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await delayedStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles?topic=b").ToString());
        await Expect(Page.GetByText("From filter B", new() { Exact = true })).ToBeVisibleAsync();
        releaseDelayed.TrySetResult();
        await Expect(Page.GetByText("From filter A", new() { Exact = true })).ToHaveCountAsync(0);

        await Page.UnrouteAsync("**/api/public/**");
        await RoutePublicApiAsync((_, _) => "[]");
        foreach (var (path, emptyTitle) in new[]
                 {
                     ("/", "No articles yet"), ("/articles", "No articles yet"), ("/archive", "The archive is empty"),
                     ("/topics", "No topics yet"), ("/tags", "No tags yet"), ("/series", "No series yet")
                 })
        {
            await Page.GotoAsync(new Uri(environment.BaseUri, path).ToString());
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = emptyTitle })).ToBeVisibleAsync();
            await Expect(Page.Locator(".content-item, .overview-link, .taxonomy-card")).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Load more" })).ToHaveCountAsync(0);
        }
    }

    private async Task RoutePublicApiAsync(Func<string, IReadOnlyDictionary<string, string>, string> response) =>
        await Page.RouteAsync("**/api/public/**", async route =>
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = response(route.Request.Url, Query(route.Request.Url)) }));

    private async Task RoutePublicApiAsync(Func<string, IReadOnlyDictionary<string, string>, Task<string>> response) =>
        await Page.RouteAsync("**/api/public/**", async route =>
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = await response(route.Request.Url, Query(route.Request.Url)) }));

    private async Task<IReadOnlyList<string>> TitlesAsync() => await Page.Locator(".content-item h2 a").AllTextContentsAsync();

    private static CatalogArticle[] Articles(params string[] titles) => titles.Select((title, index) => new CatalogArticle(
        "en", title.ToLowerInvariant().Replace(' ', '-'), title, $"Summary {title}",
        DateTimeOffset.UtcNow.AddMinutes(-index), "Catalog author", Array.Empty<object>(), Array.Empty<object>(),
        Array.Empty<string>(), null, 1)).ToArray();

    private static CatalogArticle[] ArticlePage(IReadOnlyList<CatalogArticle> all, IReadOnlyDictionary<string, string> query)
    {
        var page = int.TryParse(query.GetValueOrDefault("page"), out var parsed) ? parsed : 1;
        var pageSize = int.TryParse(query.GetValueOrDefault("pageSize"), out var size) ? size : 20;
        return all.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
    }

    private static string ArticleDetails(string title, string slug) => JsonSerializer.Serialize(new
    {
        languageCode = "en", slug, title, summary = $"Summary {title}", html = "<p>Article body.</p>",
        publishedAt = DateTimeOffset.UtcNow, updatedAt = DateTimeOffset.UtcNow, lastEditedAt = DateTimeOffset.UtcNow,
        authorDisplayName = "Catalog author", availableLocalizations = Array.Empty<object>(), topics = Array.Empty<object>(),
        series = Array.Empty<object>(), tags = Array.Empty<string>(), viewCount = 0L
    });

    private static IReadOnlyDictionary<string, string> Query(string url) => new Uri(url).Query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "", StringComparer.Ordinal);

    private sealed record CatalogArticle(
        string languageCode,
        string slug,
        string Title,
        string summary,
        DateTimeOffset publishedAt,
        string authorDisplayName,
        IReadOnlyList<object> topics,
        IReadOnlyList<object> series,
        IReadOnlyList<string> tags,
        Guid? coverMediaAssetId,
        int readingMinutes);
}
