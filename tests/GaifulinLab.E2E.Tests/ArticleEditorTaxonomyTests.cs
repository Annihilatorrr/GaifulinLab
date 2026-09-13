using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleEditorTaxonomyTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task EmptyTaxonomy_CreatesRussianTopicAndSeriesAndSelectsBoth()
    {
        var topicId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var topics = new List<object>();
        var series = new List<object>();
        await AuthenticateAsync();

        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath.TrimEnd('/');
            if (path == "/api/admin/taxonomy" && route.Request.Method == "GET")
            {
                await JsonAsync(route, new { topics, series, tags = Array.Empty<object>() });
                return;
            }

            if (path == "/api/admin/taxonomy/topics" && route.Request.Method == "POST")
            {
                var created = new
                {
                    id = topicId,
                    createdAt = DateTimeOffset.UtcNow,
                    updatedAt = DateTimeOffset.UtcNow,
                    localizations = new[] { new { id = Guid.NewGuid(), languageCode = "ru", name = "Математика", slug = "matematika", description = (string?)null } }
                };
                topics.Add(created);
                await JsonAsync(route, created, 201);
                return;
            }

            if (path == "/api/admin/taxonomy/series" && route.Request.Method == "POST")
            {
                var created = new
                {
                    id = seriesId,
                    createdAt = DateTimeOffset.UtcNow,
                    updatedAt = DateTimeOffset.UtcNow,
                    localizations = new[] { new { id = Guid.NewGuid(), languageCode = "ru", title = "Комплексные числа", slug = "complex-numbers", description = (string?)null } },
                    articles = Array.Empty<object>()
                };
                series.Add(created);
                await JsonAsync(route, created, 201);
                return;
            }

            await route.FulfillAsync(new() { Status = 404, ContentType = "application/json", Body = "{}" });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        await Page.GetByLabel("Article language").SelectOptionAsync("ru");

        var topicPopover = Page.Locator(".metadata-assignment").Filter(new() { HasText = "Topic" });
        await topicPopover.Locator("summary").ClickAsync();
        await topicPopover.GetByLabel("New topic name").FillAsync("Математика");
        await topicPopover.GetByLabel("Slug").FillAsync("matematika");
        await topicPopover.GetByRole(AriaRole.Button, new() { Name = "Create topic" }).ClickAsync();
        await Expect(Page.Locator(".metadata-chip").Filter(new() { HasText = "Математика" })).ToBeVisibleAsync();

        var seriesPopover = Page.Locator(".metadata-assignment").Filter(new() { HasText = "Series" });
        await seriesPopover.Locator("summary").ClickAsync();
        await seriesPopover.GetByLabel("New series title").FillAsync("Комплексные числа");
        await seriesPopover.GetByLabel("Slug").FillAsync("complex-numbers");
        await seriesPopover.GetByRole(AriaRole.Button, new() { Name = "Create series" }).ClickAsync();
        await Expect(Page.Locator(".metadata-chip").Filter(new() { HasText = "Комплексные числа" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TopicManager_AddsTheMissingEnglishLocalizationToTheSameTopic()
    {
        var topicId = Guid.NewGuid();
        var hasEnglishLocalization = false;
        await AuthenticateAsync();

        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath.TrimEnd('/');
            if (path == "/api/admin/taxonomy" && route.Request.Method == "GET")
            {
                var russian = new { id = Guid.NewGuid(), languageCode = "ru", name = "Математика", slug = "matematika", description = (string?)null };
                var english = new { id = Guid.NewGuid(), languageCode = "en", name = "Mathematics", slug = "mathematics", description = (string?)null };
                await JsonAsync(route, new
                {
                    topics = new[]
                    {
                        new
                        {
                            id = topicId,
                            createdAt = DateTimeOffset.UtcNow,
                            updatedAt = DateTimeOffset.UtcNow,
                            localizations = hasEnglishLocalization ? new object[] { russian, english } : [russian]
                        }
                    },
                    series = Array.Empty<object>(),
                    tags = Array.Empty<object>()
                });
                return;
            }

            if (path == $"/api/admin/taxonomy/topics/{topicId}/localizations" && route.Request.Method == "POST")
            {
                hasEnglishLocalization = true;
                await JsonAsync(route, new { id = topicId, localizations = Array.Empty<object>() }, 201);
                return;
            }

            await route.FulfillAsync(new() { Status = 404, ContentType = "application/json", Body = "{}" });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/topics").ToString());
        var topic = Page.Locator(".taxonomy-item");
        await topic.GetByText("Add localization", new() { Exact = true }).ClickAsync();
        await topic.GetByLabel("Name").FillAsync("Mathematics");
        await topic.GetByLabel("Slug").FillAsync("mathematics");
        await topic.GetByRole(AriaRole.Button, new() { Name = "Add localization" }).ClickAsync();
        await Expect(topic.GetByText("EN localization", new() { Exact = true })).ToBeVisibleAsync();
    }

    private async Task AuthenticateAsync()
    {
        var (login, password) = environment.GetAdminCredentials();
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }

    private static Task JsonAsync(IRoute route, object value, int status = 200) => route.FulfillAsync(new()
    {
        Status = status,
        ContentType = "application/json",
        Body = System.Text.Json.JsonSerializer.Serialize(value)
    });
}
