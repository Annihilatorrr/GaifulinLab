using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class PublicFailureStatesTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task CatalogErrorsAndLoadMoreErrorsRecoverAfterTheApiIsAvailableAgain()
    {
        var articles = Enumerable.Range(1, 21).Select(index => Article($"Recovered {index:00}")).ToArray();
        var failInitialLoad = true;
        var failNextPage = false;
        await Page.RouteAsync("**/api/public/articles?**", async route =>
        {
            var query = Query(route.Request.Url);
            var page = query.GetValueOrDefault("page") ?? "1";
            if (failInitialLoad || failNextPage && page == "2")
            {
                await route.FulfillAsync(new() { Status = 500, ContentType = "application/json", Body = "{}" });
                return;
            }

            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(articles.Skip((int.Parse(page) - 1) * 20).Take(20))
            });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Content is unavailable" })).ToBeVisibleAsync();

        failInitialLoad = false;
        await Page.ReloadAsync();
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(20);

        failNextPage = true;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Load more" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Content is unavailable" })).ToBeVisibleAsync();

        failNextPage = false;
        await Page.ReloadAsync();
        await Expect(Page.Locator(".content-item")).ToHaveCountAsync(20);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Load more" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task MissingPublicArticlesAndSeriesShowNotFoundWithoutBreakingTheBackLink()
    {
        await Page.RouteAsync("**/api/public/**", async route =>
        {
            var isCatalog = route.Request.Url.Contains("/articles?", StringComparison.Ordinal);
            await route.FulfillAsync(new() { Status = isCatalog ? 200 : 404, ContentType = "application/json", Body = isCatalog ? "[]" : "{}" });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/unknown").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Article not found" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Back to articles" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "No articles yet" })).ToBeVisibleAsync();

        foreach (var path in new[] { "/zz/articles/unknown", "/en/articles/unpublished-draft" })
        {
            await Page.GotoAsync(new Uri(environment.BaseUri, path).ToString());
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Article not found" })).ToBeVisibleAsync();
        }

        foreach (var path in new[] { "/en/series/unknown", "/zz/series/unknown" })
        {
            await Page.GotoAsync(new Uri(environment.BaseUri, path).ToString());
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Series not found" })).ToBeVisibleAsync();
        }
    }

    [Fact]
    public async Task FailedViewTelemetryLeavesTheArticleReadable()
    {
        await Page.RouteAsync("**/api/public/**", async route =>
        {
            if (route.Request.Url.Contains("/views", StringComparison.Ordinal))
            {
                await route.AbortAsync();
                return;
            }

            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(Article("Telemetry survives"))
            });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/telemetry-survives").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Telemetry survives" })).ToBeVisibleAsync();
        await Expect(Page.Locator("article.article-body")).ToContainTextAsync("Readable body");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Article not found" })).ToHaveCountAsync(0);
    }

    private static object Article(string title) => new
    {
        languageCode = "en", slug = title.ToLowerInvariant().Replace(' ', '-'), title, summary = "Summary",
        html = "<p>Readable body.</p>", publishedAt = DateTimeOffset.UtcNow, updatedAt = DateTimeOffset.UtcNow,
        lastEditedAt = DateTimeOffset.UtcNow, authorDisplayName = "Test author", availableLocalizations = Array.Empty<object>(),
        topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<string>(), viewCount = 3L,
        coverMediaAssetId = (Guid?)null, readingMinutes = 1
    };

    private static IReadOnlyDictionary<string, string> Query(string url) => new Uri(url).Query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "", StringComparer.Ordinal);
}
