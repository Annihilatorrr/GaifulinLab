using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using GaifulinLab.Application.Articles.Public;
using GaifulinLab.Application.Authors;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Tags;
using GaifulinLab.Domain.Topics;
using GaifulinLab.Infrastructure;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using NpgsqlTypes;
using Xunit.Abstractions;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleSearchTests(E2EEnvironment environment, ITestOutputHelper output) : PageTest
{
    [Fact]
    public async Task PostgreSql_SearchScopesRankingPaginationAndLiveChanges()
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var search = scope.ServiceProvider.GetRequiredService<IArticleSearch>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var owner = await db.Users.Select(u => u.Id).FirstAsync();
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");
        var marker = Tag.Create("search-" + suffix);
        var cpp = Tag.Create("C++ " + suffix);
        var sharp = Tag.Create("C# " + suffix);
        var dotnet = Tag.Create(".NET " + suffix);
        var topic = Topic.Create("en", "Astronomy " + suffix, "astronomy-" + suffix, null, now);
        db.Tags.AddRange(marker, cpp, sharp, dotnet);
        db.Topics.Add(topic);
        Article Make(string title, string body = "We are running experiments.", DateTimeOffset? date = null)
        {
            var article = Article.Create(owner, "en", now, title, "Short overview", body, "search-" + Guid.NewGuid().ToString("N"));
            article.AssignTag(marker, now);
            article.AssignTopic(topic, now);
            article.PublishLocalization("en", date ?? now);
            db.Articles.Add(article);
            return article;
        }
        var titleMatch = Make("Quasar in the title", "A distant star.");
        titleMatch.AssignTag(cpp, now);
        titleMatch.AddLocalization("ru", now, "Цифровая обработка", null, "Разработка цифровых фильтров и обработка сигналов.", "filters-" + suffix);
        titleMatch.PublishLocalization("ru", now);
        var bodyMatch = Make("A distant star", "The quasar appears in the middle of this paragraph.");
        bodyMatch.AssignTag(sharp, now);
        bodyMatch.AssignTag(dotnet, now);
        for (var i = 0; i < 21; i++) Make($"Experiment {i:00}");
        var old = Make("Historical experiment", date: now.AddYears(-2));
        var draft = Make("Invisible quasar"); draft.UnpublishLocalization("en", now);
        var deleted = Make("Deleted quasar"); deleted.Delete(now);
        var newDraft = Article.Create(owner, "en", now, "Draft quasar", null, "quasar", "draft-" + suffix);
        newDraft.AssignTag(marker, now); db.Articles.Add(newDraft);
        await db.SaveChangesAsync();
        ArticleSearchRequest Request(string? query = null, string area = "all", int page = 1) => new() { Query = query, Scope = area, Page = page, Tag = [marker.Name] };

        var first = await search.SearchAsync(Request(), default);
        var second = await search.SearchAsync(Request(page: 2), default);
        var last = await search.SearchAsync(Request(page: int.MaxValue), default);
        Assert.Equal(24, first.TotalCount); Assert.Equal(3, first.TotalPages);
        Assert.Equal(10, first.Items.Count); Assert.Equal(10, second.Items.Count);
        Assert.Equal(3, last.Page); Assert.Equal(4, last.Items.Count);
        Assert.Equal(24, first.Items.Concat(second.Items).Concat(last.Items).Select(x => x.Slug).Distinct().Count());
        Assert.Equal(first.Items.Select(x => x.Slug), (await search.SearchAsync(Request(), default)).Items.Select(x => x.Slug));
        var ranked = await search.SearchAsync(Request("quasar"), default);
        Assert.Equal(2, ranked.TotalCount); Assert.Equal("Quasar in the title", ranked.Items[0].Title);
        Assert.Single((await search.SearchAsync(Request("quasar", "title"), default)).Items);
        var inBody = await search.SearchAsync(Request("quasar", "content"), default);
        Assert.Single(inBody.Items); Assert.Contains("\uE000quasar\uE001", inBody.Items[0].SearchSnippet!);
        Assert.Equal(24, (await search.SearchAsync(Request("astronomy", "topics"), default)).TotalCount);
        Assert.Equal(22, (await search.SearchAsync(Request("run", "content"), default)).TotalCount);
        Assert.Equal(1, (await search.SearchAsync(Request("C++", "tags"), default)).TotalCount);
        Assert.Equal(1, (await search.SearchAsync(Request("C#", "tags"), default)).TotalCount);
        Assert.Equal(1, (await search.SearchAsync(Request(".NET", "tags"), default)).TotalCount);
        titleMatch.UpdateLocalization("en", titleMatch.Localizations.First(x => x.LanguageCode == "en").Title, null,
            string.Join(' ', Enumerable.Repeat("Distant galaxies", 100)) + " Learning C++ programming.",
            titleMatch.Localizations.First(x => x.LanguageCode == "en").Slug, now);
        await db.SaveChangesAsync();
        var technicalSnippet = Assert.Single((await search.SearchAsync(Request("C++", "content"), default)).Items).SearchSnippet!;
        Assert.Contains("C++", technicalSnippet.Replace("\uE000", "").Replace("\uE001", ""));
        Assert.Equal(0, (await search.SearchAsync(Request("absentword"), default)).TotalCount);
        var russian = Request("фильтры", "content"); russian.LanguageCode = "ru";
        Assert.Single((await search.SearchAsync(russian, default)).Items);
        var recent = Request(); recent.Period = "year"; recent.Topic = topic.Localizations.Single().Slug;
        Assert.Equal(23, (await search.SearchAsync(recent, default)).TotalCount);
        recent.Tag = [cpp.Name, sharp.Name];
        Assert.Equal(2, (await search.SearchAsync(recent, default)).TotalCount);
        var oldest = Request(); oldest.Sort = "oldest";
        Assert.Equal(old.Localizations.Single().Slug, (await search.SearchAsync(oldest, default)).Items[0].Slug);

        bodyMatch.UpdateLocalization("en", "A distant star", null, "Replacement pulsar content", bodyMatch.Localizations.Single().Slug, now);
        topic.UpdateLocalization("en", "Cosmology " + suffix, topic.Localizations.Single().Slug, null, now);
        cpp.Rename("Rust " + suffix);
        titleMatch.ReplaceTopics([], now);
        await db.SaveChangesAsync();
        Assert.Empty((await search.SearchAsync(Request("quasar", "content"), default)).Items);
        Assert.Single((await search.SearchAsync(Request("pulsar", "content"), default)).Items);
        Assert.Empty((await search.SearchAsync(Request("astronomy", "topics"), default)).Items);
        Assert.Equal(23, (await search.SearchAsync(Request("cosmology", "topics"), default)).TotalCount);
        Assert.Empty((await search.SearchAsync(Request("C++", "tags"), default)).Items);
        Assert.Single((await search.SearchAsync(Request("Rust", "tags"), default)).Items);
        titleMatch.UnpublishLocalization("en", now);
        await db.SaveChangesAsync();
        Assert.Empty((await search.SearchAsync(Request("quasar"), default)).Items);

        // Backfill uses the same Markdown extraction, preserves edit versions and is repeatable.
        var id = bodyMatch.Localizations.Single().Id;
        var version = bodyMatch.Localizations.Single().Version;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE article_localizations SET \"SearchText\" = NULL WHERE \"Id\" = {id}");
        await db.BackfillArticleSearchAsync(); await db.BackfillArticleSearchAsync();
        var filled = await db.ArticleLocalizations.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Contains("pulsar", filled.SearchText); Assert.Equal(version, filled.Version);

        // Representative local timing, not a machine-independent latency assertion.
        for (var i = 0; i < 1000; i++) Make("Load sample " + i, string.Join(' ', Enumerable.Repeat("signal processing experiment", 100)));
        await db.SaveChangesAsync();
        var watch = Stopwatch.StartNew();
        var large = await search.SearchAsync(Request("signal", "content"), default);
        output.WriteLine($"Search across 1,000 added 300-word articles: {watch.ElapsedMilliseconds} ms; {large.TotalCount} matches; {large.Items.Count} returned.");
        Assert.Equal(1000, large.TotalCount); Assert.Equal(10, large.Items.Count);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            var indexedQuery = db.ArticleLocalizations
                .Where(localization => EF.Property<NpgsqlTsVector>(localization, "BodySearchVector")
                    .Matches(EF.Functions.PlainToTsQuery("english", "signal")))
                .Select(localization => localization.Id);
            command.CommandText = "EXPLAIN " + indexedQuery.ToQueryString();
            await using var reader = await command.ExecuteReaderAsync();
            var lines = new List<string>();
            while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
            Assert.Contains("ix_article_search_body_vector", string.Join('\n', lines));
        }
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Browser_SearchUrlFiltersPagesThemesAndMobile()
    {
        Page.PageError += (_, error) => output.WriteLine("Browser error: " + error);
        Page.Response += (_, response) => { if (response.Status >= 400) output.WriteLine($"HTTP {response.Status}: {response.Url}"); };
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await db.Users.Select(u => u.Id).FirstAsync();
        var token = "browsersearch" + Guid.NewGuid().ToString("N");
        var tag = Tag.Create(token); db.Tags.Add(tag);
        for (var i = 0; i < 23; i++)
        {
            var article = Article.Create(owner, "en", DateTimeOffset.UtcNow, $"Search interface lesson {i:00}", "Discover practical engineering techniques.", "A searchable paragraph about interfaces and accessible navigation.", token + "-" + i);
            article.AssignTag(tag, DateTimeOffset.UtcNow); article.PublishLocalization("en", DateTimeOffset.UtcNow); db.Articles.Add(article);
        }
        await db.SaveChangesAsync();
        using var api = new HttpClient { BaseAddress = environment.ApiBaseUri };
        var response = await api.GetFromJsonAsync<ArticleSearchResponse>($"/api/public/search?tag={token}");
        Assert.Equal(23, response!.TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/api/public/search?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/api/public/search?scope=wrong")).StatusCode);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/search?tag={token}").ToString());
        await Expect(Page.Locator(".search-card")).ToHaveCountAsync(10, new() { Timeout = 15000 });
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("23 articles found");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Page 2", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".page-summary")).ToHaveTextAsync("Page 2 of 3");
        var secondTitles = await Page.Locator(".search-card h2").AllTextContentsAsync();
        await Page.ReloadAsync();
        await Expect(Page.Locator(".page-summary")).ToHaveTextAsync("Page 2 of 3");
        Assert.Equal(secondTitles, await Page.Locator(".search-card h2").AllTextContentsAsync());
        await Page.GetByRole(AriaRole.Link, new() { Name = "Next page" }).ClickAsync();
        await Expect(Page.Locator(".search-card")).ToHaveCountAsync(3);
        await Page.GoBackAsync();
        await Expect(Page.Locator(".page-summary")).ToHaveTextAsync("Page 2 of 3");
        await Page.GetByLabel("Search articles", new() { Exact = true }).FillAsync("interface");
        await Page.GetByLabel("Search articles", new() { Exact = true }).PressAsync("Enter");
        await Expect(Page.Locator(".page-summary")).ToHaveTextAsync("Page 1 of 3");
        await Expect(Page.Locator(".search-card h2 mark").First).ToBeVisibleAsync();
        await Page.GetByLabel("Search in", new() { Exact = true }).SelectOptionAsync("title");
        await Expect(Page.Locator(".search-card")).ToHaveCountAsync(10);
        await Page.GetByLabel("Sort by", new() { Exact = true }).SelectOptionAsync("oldest");
        await Expect(Page.Locator(".search-card h2").First).ToHaveTextAsync("Search interface lesson 00");
        Directory.CreateDirectory(Path.Combine("TestResults", "search"));
        await Page.SetViewportSizeAsync(1440, 1100);
        await Page.EvaluateAsync("window.scrollTo({top: 0, behavior: 'instant'}); document.activeElement?.blur()");
        await Page.EvaluateAsync("document.documentElement.dataset.theme = 'light'");
        await Page.ScreenshotAsync(new() { Path = Path.Combine("TestResults", "search", "desktop-light.png"), FullPage = true });
        await Page.EvaluateAsync("document.documentElement.dataset.theme = 'dark'");
        await Page.ScreenshotAsync(new() { Path = Path.Combine("TestResults", "search", "desktop-dark.png"), FullPage = true });
        await Page.SetViewportSizeAsync(390, 844);
        Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await Page.ScreenshotAsync(new() { Path = Path.Combine("TestResults", "search", "mobile-dark.png"), FullPage = true });
        await Page.EvaluateAsync("document.documentElement.dataset.theme = 'light'");
        await Page.ScreenshotAsync(new() { Path = Path.Combine("TestResults", "search", "mobile-light.png"), FullPage = true });
        await Page.GetByLabel("Search articles", new() { Exact = true }).FillAsync("no-such-article");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = false }).First.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "No articles found" })).ToBeVisibleAsync();
        // Exercise a failed request and recovery without changing server availability.
        await Page.RouteAsync("**/api/public/search?**", route => route.AbortAsync());
        await Page.GetByLabel("Search articles", new() { Exact = true }).FillAsync("interface");
        await Page.GetByLabel("Search articles", new() { Exact = true }).PressAsync("Enter");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await Page.UnrouteAsync("**/api/public/search?**");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();
        await Expect(Page.Locator(".search-card")).ToHaveCountAsync(10);

        await Page.Locator(".tag-picker summary").ClickAsync();
        await Page.Locator("#tag-filter").FillAsync(token);
        await Expect(Page.Locator(".tag-options input[type=checkbox]")).ToHaveCountAsync(1);
        await Page.Locator("#tag-filter").PressAsync("Escape");
        await Expect(Page.Locator(".tag-picker")).Not.ToHaveAttributeAsync("open", "");
        await Expect(Page.Locator(".tag-picker summary")).ToBeFocusedAsync();

        var oldStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync("**/api/public/search?**", async route =>
        {
            if (!route.Request.Url.Contains("q=delayedquery")) { await route.ContinueAsync(); return; }
            oldStarted.TrySetResult();
            await releaseOld.Task;
            try { await route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"items\":[],\"totalCount\":0,\"page\":1,\"pageSize\":10,\"totalPages\":0}" }); }
            catch (PlaywrightException) { /* The browser may already have cancelled this request. */ }
            finally { oldFinished.TrySetResult(); }
        });
        await Page.GetByLabel("Search articles", new() { Exact = true }).FillAsync("delayedquery");
        await Page.GetByLabel("Search articles", new() { Exact = true }).PressAsync("Enter");
        await oldStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Page.GetByLabel("Search articles", new() { Exact = true }).FillAsync("interface");
        await Page.GetByLabel("Search articles", new() { Exact = true }).PressAsync("Enter");
        await Expect(Page.Locator(".search-card")).ToHaveCountAsync(10);
        releaseOld.TrySetResult();
        await oldFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Expect(Page.Locator(".search-card")).ToHaveCountAsync(10);
    }

    private static ServiceProvider CreateServices()
    {
        var connection = Environment.GetEnvironmentVariable("GAIFULINLAB_E2E_CONNECTION_STRING")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connection }).Build();
        var services = new ServiceCollection().AddInfrastructure(configuration);
        // Disable retries for the rollback transaction used by the database test.
        services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAuthorDisplayNameLookup, TestAuthors>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Browser_CoverUploadPersistsAndCanBeRemoved()
    {
        var (login, password) = environment.GetAdminCredentials();
        var token = "searchcover" + Guid.NewGuid().ToString("N");
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/admin/articles$"));
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        await Page.GetByLabel("Article title").FillAsync(token);
        await Page.GetByLabel("Article Markdown").FillAsync(string.Join(' ', Enumerable.Repeat("A useful paragraph about search.", 50)));
        await Page.GetByText("+ Add cover", new() { Exact = true }).ClickAsync();
        await Page.GetByLabel("Upload cover", new() { Exact = true }).SetInputFilesAsync(new FilePayload
        {
            Name = "search-cover.png", MimeType = "image/png",
            Buffer = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")
        });
        await Expect(Page.GetByAltText("Article cover")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/admin/articles/[0-9a-f-]{36}$"));
        var editorUrl = Page.Url;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");
        await Page.GotoAsync(new Uri(environment.BaseUri, "/search?q=" + token).ToString());
        await Expect(Page.Locator(".search-card")).ToHaveCountAsync(1);
        var image = Page.Locator(".search-cover img");
        await Expect(image).ToBeVisibleAsync();
        Assert.True(await image.EvaluateAsync<bool>("img => img.complete && img.naturalWidth > 0"));
        await Expect(Page.Locator(".article-meta")).ToContainTextAsync("2 min read");
        Directory.CreateDirectory(Path.Combine("TestResults", "search"));
        await Page.ScreenshotAsync(new() { Path = Path.Combine("TestResults", "search", "with-cover.png"), FullPage = true });
        await Page.GotoAsync(editorUrl);
        await Page.GetByText("Change cover", new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByAltText("Article cover")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove cover", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeDisabledAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByText("+ Add cover", new() { Exact = true })).ToBeVisibleAsync();
        await Page.GotoAsync(new Uri(environment.BaseUri, "/search?q=" + token).ToString());
        await Expect(Page.Locator(".search-card")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".search-cover")).ToHaveCountAsync(0);
    }

    private sealed class TestAuthors : IAuthorDisplayNameLookup
    {
        public Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(userIds.Distinct().ToDictionary(id => id, _ => "Search test author"));
    }
}
