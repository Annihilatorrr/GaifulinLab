using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class HomePageTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task Home_ShowsOnlyTheLatestThreeArticlesAndFiveTaxonomyItems()
    {
        await MockHomeApiAsync();
        await Page.GotoAsync(environment.BaseUri.ToString());

        var articles = Page.Locator(".overview-column:nth-child(1) .overview-link");
        var topics = Page.Locator(".overview-column:nth-child(2) .overview-link");
        var series = Page.Locator(".overview-column:nth-child(3) .overview-link");
        await Expect(articles).ToHaveCountAsync(3);
        await Expect(topics).ToHaveCountAsync(5);
        await Expect(series).ToHaveCountAsync(5);
        Assert.Equal(
            ["Latest article 4", "Latest article 3", "Latest article 2"],
            await articles.Locator("strong").AllTextContentsAsync());
        await Expect(articles.First).ToContainTextAsync("By Author 4");
        await Expect(topics.First).ToContainTextAsync("1 article(s)");
        await Expect(series.First).ToContainTextAsync("1 article(s)");
    }

    [Fact]
    public async Task Home_LinksLeadToTheirPublicDestinations()
    {
        await MockHomeApiAsync();
        await Page.GotoAsync(environment.BaseUri.ToString());

        await Page.Locator(".overview-column:nth-child(1) .overview-link").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/en/articles/latest-4$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Latest article 4" })).ToBeVisibleAsync();
        await Page.GotoAsync(environment.BaseUri.ToString());
        await Page.Locator(".overview-column:nth-child(2) .overview-link").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/articles\\?topic=topic-1$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Articles" })).ToBeVisibleAsync();
        await Page.GotoAsync(environment.BaseUri.ToString());
        await Page.Locator(".overview-column:nth-child(3) .overview-link").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/en/series/series-1$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Series 1" })).ToBeVisibleAsync();

        await Page.GotoAsync(environment.BaseUri.ToString());
        await Page.GetByRole(AriaRole.Link, new() { Name = "View all" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/articles$"));
        await Page.GoBackAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "All topics" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/topics$"));
        await Page.GoBackAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "All series" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/series$"));

        await Page.GotoAsync(environment.BaseUri.ToString());
        await Page.GetByRole(AriaRole.Link, new() { Name = "Explore topics" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/topics$"));
        await Page.GoBackAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Browse latest articles" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/articles$"));
    }

    [Theory]
    [InlineData("articles")]
    [InlineData("topics")]
    [InlineData("series")]
    public async Task Home_KeepsHealthyBlocksVisibleWhenOnePublicRequestFails(string unavailableBlock)
    {
        var failRequestedBlock = true;
        await Page.RouteAsync("**/api/public/**", async route =>
        {
            var url = route.Request.Url;
            var block = url.Contains("/topics/", StringComparison.Ordinal) ? "topics"
                : url.Contains("/series", StringComparison.Ordinal) ? "series"
                : "articles";
            if (failRequestedBlock && block == unavailableBlock)
            {
                await route.FulfillAsync(new() { Status = 500, ContentType = "application/json", Body = "{}" });
                return;
            }

            var body = block switch
            {
                "topics" => "[{\"languageCode\":\"en\",\"slug\":\"topic\",\"name\":\"Healthy topic\",\"description\":\"\",\"articleCount\":1}]",
                "series" => "[{\"languageCode\":\"en\",\"slug\":\"series\",\"title\":\"Healthy series\",\"description\":\"\",\"articleCount\":1}]",
                _ => "[{\"languageCode\":\"en\",\"slug\":\"article\",\"title\":\"Healthy article\",\"summary\":\"\",\"publishedAt\":\"2026-01-01T00:00:00Z\",\"authorDisplayName\":\"Author\",\"topics\":[],\"series\":[],\"tags\":[],\"readingMinutes\":1}]"
            };
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = body });
        });

        await Page.GotoAsync(environment.BaseUri.ToString());
        foreach (var (block, expected) in new (string block, string expected)[]
                 {
                     ("articles", "Healthy article"), ("topics", "Healthy topic"), ("series", "Healthy series")
                 }.Where(item => item.block != unavailableBlock))
        {
            await Expect(Page.GetByText(expected, new() { Exact = true })).ToBeVisibleAsync();
        }

        failRequestedBlock = false;
        await Page.ReloadAsync();
        var restored = unavailableBlock switch
        {
            "articles" => "Healthy article",
            "topics" => "Healthy topic",
            _ => "Healthy series"
        };
        await Expect(Page.GetByText(restored, new() { Exact = true })).ToBeVisibleAsync();
    }

    private async Task MockHomeApiAsync()
    {
        var articles = Enumerable.Range(1, 4).Reverse().Select(index => new
        {
            languageCode = "en",
            slug = $"latest-{index}",
            title = $"Latest article {index}",
            summary = $"Summary {index}",
            publishedAt = DateTimeOffset.UtcNow.AddDays(-index),
            authorDisplayName = $"Author {index}",
            topics = Array.Empty<object>(),
            series = Array.Empty<object>(),
            tags = Array.Empty<string>(),
            coverMediaAssetId = (Guid?)null,
            readingMinutes = 1
        }).ToArray();
        var topics = Enumerable.Range(1, 6).Select(index => new
        {
            languageCode = "en", slug = $"topic-{index}", name = $"Topic {index}",
            description = $"Topic description {index}", articleCount = index
        }).ToArray();
        var series = Enumerable.Range(1, 6).Select(index => new
        {
            languageCode = "en", slug = $"series-{index}", title = $"Series {index}",
            description = $"Series description {index}", articleCount = index
        }).ToArray();

        await Page.RouteAsync("**/api/public/**", async route =>
        {
            var url = route.Request.Url;
            var body = url.Contains("/api/public/articles/en/latest-4/views", StringComparison.Ordinal)
                ? "{\"viewCount\":1}"
                : url.Contains("/api/public/articles/en/latest-4", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new
                {
                    languageCode = "en", slug = "latest-4", title = "Latest article 4", summary = "Summary 4",
                    html = "<p>Latest article body.</p>", publishedAt = DateTimeOffset.UtcNow,
                    updatedAt = DateTimeOffset.UtcNow, lastEditedAt = DateTimeOffset.UtcNow,
                    authorDisplayName = "Author 4", availableLocalizations = Array.Empty<object>(),
                    topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<string>(), viewCount = 0L
                })
                : url.Contains("/api/public/series/en/series-1", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(new
                    {
                        languageCode = "en", slug = "series-1", title = "Series 1", description = "Series description 1",
                        articles = new[] { new { position = 1, slug = "latest-4", title = "Latest article 4", summary = "Summary 4", authorDisplayName = "Author 4" } }
                    })
                : url.Contains("/api/public/topics/", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(topics)
                : url.Contains("/api/public/series", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(series)
                    : JsonSerializer.Serialize(articles);
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = body });
        });
    }
}
