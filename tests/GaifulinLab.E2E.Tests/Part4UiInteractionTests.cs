using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class Part4UiInteractionTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task NavigationAndAccountLinks_WorkInDesktopAndMobileMenus()
    {
        await ConfigureFeaturesAsync(signIn: true, registration: true);
        await RouteEmptyPublicApiAsync();
        await RouteEmptyAdminApiAsync();
        await Page.GotoAsync(new Uri(environment.BaseUri, "/").ToString());

        var desktop = Page.GetByRole(AriaRole.Navigation, new() { Name = "Primary navigation" });
        await Expect(desktop.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
        await Expect(desktop.GetByRole(AriaRole.Link, new() { Name = "Register", Exact = true })).ToBeVisibleAsync();
        foreach (var (name, path) in PublicNavigationLinks)
        {
            await Expect(desktop.GetByRole(AriaRole.Link, new() { Name = name, Exact = true })).ToHaveAttributeAsync("href", path);
        }

        await desktop.GetByRole(AriaRole.Link, new() { Name = "About", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/about$"));
        await Page.GetByLabel("Gaifulin Lab home").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/$"));

        await Page.SetViewportSizeAsync(390, 844);
        await Page.GetByLabel("Open navigation").ClickAsync();
        var mobile = Page.GetByRole(AriaRole.Navigation, new() { Name = "Mobile navigation" });
        await Expect(mobile.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
        await mobile.GetByRole(AriaRole.Link, new() { Name = "Archive", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/archive$"));

        await AuthenticateAsync("Author");
        await Page.ReloadAsync();
        await Page.GetByLabel("Open navigation").ClickAsync();
        mobile = Page.GetByRole(AriaRole.Navigation, new() { Name = "Mobile navigation" });
        await Expect(mobile.GetByRole(AriaRole.Link, new() { Name = "My articles", Exact = true })).ToBeVisibleAsync();
        await Expect(mobile.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToHaveCountAsync(0);
        await Expect(mobile.GetByRole(AriaRole.Link, new() { Name = "Register", Exact = true })).ToHaveCountAsync(0);
        await mobile.GetByRole(AriaRole.Link, new() { Name = "My articles", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My articles" })).ToBeVisibleAsync();
        await Page.GetByLabel("Return to Gaifulin Lab").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/$"));
    }

    [Fact]
    public async Task DisabledAccountLinkFlags_HideMenusButDoNotDisableDirectRoutes()
    {
        await ConfigureFeaturesAsync(signIn: false, registration: false);
        await RouteEmptyPublicApiAsync();
        await Page.GotoAsync(new Uri(environment.BaseUri, "/").ToString());
        var desktop = Page.GetByRole(AriaRole.Navigation, new() { Name = "Primary navigation" });
        await Expect(desktop.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToHaveCountAsync(0);
        await Expect(desktop.GetByRole(AriaRole.Link, new() { Name = "Register", Exact = true })).ToHaveCountAsync(0);
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GetByLabel("Open navigation").ClickAsync();
        var mobile = Page.GetByRole(AriaRole.Navigation, new() { Name = "Mobile navigation" });
        await Expect(mobile.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToHaveCountAsync(0);
        await Expect(mobile.GetByRole(AriaRole.Link, new() { Name = "Register", Exact = true })).ToHaveCountAsync(0);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Register here" })).ToHaveCountAsync(0);
        await Page.GotoAsync(new Uri(environment.BaseUri, "/register").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Create an account" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task StaticAndUnknownRoutes_SurviveDirectNavigationAndReload()
    {
        await RouteEmptyPublicApiAsync();
        foreach (var (path, heading) in new[]
        {
            ("/about", "Gaifulin Lab"),
            ("/search", "Find your next idea."),
            ("/not-found", "Not Found"),
            ("/a-route-that-does-not-exist", "Not Found")
        })
        {
            await Page.GotoAsync(new Uri(environment.BaseUri, path).ToString());
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true })).ToBeVisibleAsync();
            await Page.ReloadAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true })).ToBeVisibleAsync();
        }
    }

    [Fact]
    public async Task Theme_UsesSystemPreferencePersistsAcrossLayoutsAndWorksWithoutStorage()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await RouteEmptyPublicApiAsync();
        await Page.GotoAsync(new Uri(environment.BaseUri, "/about").ToString());
        var toggle = Page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" });
        await Expect(toggle).ToHaveAttributeAsync("aria-pressed", "true");
        await toggle.ClickAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Switch to dark theme" })).ToHaveAttributeAsync("aria-pressed", "false");

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
        await Page.ReloadAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");

        await Page.AddInitScriptAsync("""
            for (const method of ['getItem', 'setItem', 'removeItem', 'clear']) {
              Storage.prototype[method] = () => { throw new DOMException('Storage disabled'); };
            }
            """);
        await Page.ReloadAsync();
        toggle = Page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" });
        await toggle.ClickAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
    }

    [Fact]
    public async Task EditorModes_AdaptToMobileWithoutLosingTextOrPreview()
    {
        var article = new EditorArticle();
        await AuthenticateAsync();
        await RouteEditorAsync(article);
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        var markdown = Page.GetByLabel("Article Markdown");
        await markdown.FillAsync("Text kept across editor modes");
        await Page.Locator(".editor-tabs button", new() { HasText = "Preview" }).ClickAsync();
        await Expect(Page.Locator(".article-editor")).ToHaveAttributeAsync("data-active-pane", "Preview");
        await Expect(Page.Locator("article.article-preview")).ToContainTextAsync("Text kept across editor modes");
        await Page.Locator(".editor-tabs button", new() { HasText = "Edit" }).ClickAsync();
        await Expect(markdown).ToHaveValueAsync("Text kept across editor modes");
        await Page.Locator(".editor-tabs button", new() { HasText = "Split" }).ClickAsync();
        await Expect(Page.Locator(".article-editor")).ToHaveAttributeAsync("data-active-pane", "Split");

        await Page.SetViewportSizeAsync(390, 844);
        await Expect(Page.Locator(".editor-tabs .split-mode")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".editor-tabs button", new() { HasText = "Edit" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".editor-tabs button", new() { HasText = "Preview" })).ToBeVisibleAsync();
        await Expect(markdown).ToHaveValueAsync("Text kept across editor modes");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true })).ToBeVisibleAsync();

        await Page.SetViewportSizeAsync(1280, 900);
        await Expect(Page.Locator(".editor-tabs .split-mode")).ToBeVisibleAsync();
        await Expect(markdown).ToHaveValueAsync("Text kept across editor modes");
    }

    [Fact]
    public async Task Splitter_ClampsSupportsKeyboardAndDoubleClickAndRestoresSavedPosition()
    {
        var article = new EditorArticle();
        await AuthenticateAsync();
        await RouteEditorAsync(article);
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        var splitter = Page.GetByRole(AriaRole.Separator);
        var workspace = Page.Locator(".editor-workspace");
        var bounds = Assert.IsType<LocatorBoundingBoxResult>(await workspace.BoundingBoxAsync());

        await DragSplitterAsync(splitter, bounds.X + 1);
        await Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "30");
        await DragSplitterAsync(splitter, bounds.X + bounds.Width - 1);
        await Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "70");
        await splitter.FocusAsync();
        await splitter.PressAsync("ArrowLeft");
        await Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "65");
        await splitter.PressAsync("End");
        await Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "70");
        await splitter.DblClickAsync();
        await Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "50");
        await splitter.PressAsync("ArrowRight");
        await Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "55");
        await Page.ReloadAsync();
        await Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "55");
    }

    [Fact]
    public async Task TypographySettings_PersistApplyPubliclyAndAreOverriddenByClampedQueryValues()
    {
        var article = new EditorArticle { Status = 1, Slug = "typography" };
        await AuthenticateAsync();
        await RouteEditorAsync(article);
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Page.GetByText("Preview settings", new() { Exact = true }).ClickAsync();
        var paragraphSpacing = Page.GetByRole(AriaRole.Slider, new() { Name = "Paragraph spacing" });

        // The long label must leave enough track width for precise pointer adjustment.
        var paragraphSpacingBounds = Assert.IsType<LocatorBoundingBoxResult>(await paragraphSpacing.BoundingBoxAsync());
        Assert.True(paragraphSpacingBounds.Width >= 96, $"Paragraph spacing slider width was {paragraphSpacingBounds.Width}px.");

        await Page.GetByRole(AriaRole.Slider, new() { Name = "Line spacing" }).FillAsync("2.1");
        await paragraphSpacing.FillAsync("1.4");
        await Expect(Page.Locator(".article-editor")).ToHaveAttributeAsync("style", new Regex("line-height: 2.1.*block-spacing: 1.4rem"));
        await Page.ReloadAsync();
        await Expect(Page.Locator(".article-editor")).ToHaveAttributeAsync("style", new Regex("line-height: 2.1.*block-spacing: 1.4rem"));

        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/typography").ToString());
        await Expect(Page.Locator("main.article-page")).ToHaveAttributeAsync("style", new Regex("line-height: 2.1.*block-spacing: 1.4rem"));
        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/typography?lineHeight=1.2&blockSpacing=0.3").ToString());
        await Expect(Page.Locator("main.article-page")).ToHaveAttributeAsync("style", new Regex("line-height: 1.2.*block-spacing: 0.3rem"));
        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/typography?lineHeight=9&blockSpacing=-2").ToString());
        await Expect(Page.Locator("main.article-page")).ToHaveAttributeAsync("style", new Regex("line-height: 2\\.2(?:0)?.*block-spacing: 0\\.1(?:0)?rem"));
    }

    [Fact]
    public async Task KeyboardSaveAndTabIndent_TargetTheCurrentArticleAndPersistChanges()
    {
        var first = new EditorArticle { Title = "First" };
        var second = new EditorArticle { Title = "Second" };
        await Page.AddInitScriptAsync("window.saveShortcutPrevented = false; window.addEventListener('keydown', event => { if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') window.saveShortcutPrevented = event.defaultPrevented; });");
        await AuthenticateAsync();
        await RouteEditorsAsync(first, second);

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{first.Id}").ToString());
        var markdown = Page.GetByLabel("Article Markdown");
        await markdown.FillAsync("First keyboard save");
        Assert.True(await DispatchSaveShortcutAsync());
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeDisabledAsync();
        Assert.True(await Page.EvaluateAsync<bool>("window.saveShortcutPrevented"));

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{second.Id}").ToString());
        markdown = Page.GetByLabel("Article Markdown");
        await markdown.FillAsync("alpha beta gamma");
        Assert.True(await DispatchSaveShortcutAsync());
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeDisabledAsync();
        Assert.Equal("alpha beta gamma", second.Markdown);

        await markdown.EvaluateAsync("editor => { editor.focus(); editor.setSelectionRange(6, 10); }");
        await markdown.PressAsync("Tab");
        await Expect(markdown).ToHaveValueAsync("alpha      gamma");
        await Expect(Page.Locator("article.article-preview")).ToContainTextAsync("alpha      gamma");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeDisabledAsync();
        await Page.ReloadAsync();
        await Expect(markdown).ToHaveValueAsync("alpha      gamma");
        Assert.Equal("First keyboard save", first.Markdown);
        Assert.Equal("alpha      gamma", second.Markdown);
    }

    [Fact]
    public async Task EscapeRestoresPopoverFocusAndSkipLinkMovesFocusBeforeKeyboardNavigation()
    {
        var article = new EditorArticle();
        await AuthenticateAsync();
        await RouteEditorAsync(article, includeTaxonomy: true);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());

        foreach (var selector in new[] { ".metadata-assignment:has-text('Topic') details", ".metadata-assignment:has-text('Series') details", "details.action-menu" })
        {
            var menu = Page.Locator(selector);
            var trigger = menu.Locator("summary");
            await trigger.ClickAsync();
            await menu.Locator("input, button").First.FocusAsync();
            await Page.Keyboard.PressAsync("Escape");
            await Expect(menu).Not.ToHaveAttributeAsync("open", "");
            Assert.True(await trigger.EvaluateAsync<bool>("element => element === document.activeElement"));
        }

        var skip = Page.GetByRole(AriaRole.Link, new() { Name = "Skip to content" });
        await skip.FocusAsync();
        Assert.True(await skip.EvaluateAsync<bool>("element => element === document.activeElement"));
        await Page.Keyboard.PressAsync("Enter");
        Assert.Equal("admin-main-content", await Page.EvaluateAsync<string>("document.activeElement.id"));
        await Page.GetByLabel("Return to Gaifulin Lab").FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page).ToHaveURLAsync(new Regex("/$"));
        var heading = Page.Locator("h1").First;
        await Expect(heading).ToBeVisibleAsync();
        Assert.True(await heading.EvaluateAsync<bool>("element => element === document.activeElement"));
    }

    [Fact]
    public async Task OpenArticle_OpensPublishedLocalizationInNewTabAndDraftHasNoActiveLink()
    {
        var article = new EditorArticle { Status = 1, Slug = "published-en", Markdown = "Published English body" };
        article.RussianStatus = 0;
        await AuthenticateAsync();
        await RouteEditorOnContextAsync(article);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Page.GetByLabel("More article actions").ClickAsync();
        var popup = await Page.RunAndWaitForPopupAsync(() =>
            Page.GetByRole(AriaRole.Link, new() { Name = "Open article", Exact = true }).ClickAsync());
        await popup.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(popup).ToHaveURLAsync(new Regex("/en/articles/published-en$"));
        await Expect(popup.GetByRole(AriaRole.Heading, new() { Name = "Published article" })).ToBeVisibleAsync();
        await Expect(popup.Locator("article.article-body")).ToContainTextAsync("Published English body");
        await popup.CloseAsync();

        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        var actions = Page.Locator("details.action-menu");
        if (await actions.GetAttributeAsync("open") is null)
        {
            await Page.GetByLabel("More article actions").ClickAsync();
        }
        await Expect(Page.Locator("details.action-menu span[aria-disabled='true']").Filter(new() { HasText = "Open article" })).ToBeVisibleAsync();
        await Expect(Page.Locator("details.action-menu a", new() { HasText = "Open article" })).ToHaveCountAsync(0);
    }

    private static readonly (string Name, string Path)[] PublicNavigationLinks =
    [
        ("Topics", "/topics"), ("Series", "/series"), ("Tags", "/tags"),
        ("Archive", "/archive"), ("About", "/about")
    ];

    private async Task ConfigureFeaturesAsync(bool signIn, bool registration) =>
        await Page.RouteAsync("**/appsettings.json", route => JsonAsync(route, new
        {
            Api = new { BaseUrl = "/" },
            Features = new { SignInLinkEnabled = signIn, RegistrationLinkEnabled = registration }
        }));

    private async Task AuthenticateAsync(string role = "Admin")
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{{\"sub\":\"part4-user\",\"role\":\"{role}\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Page.AddInitScriptAsync($"sessionStorage.setItem('gaifulinlab.admin.access_token','header.{payload}.signature');");
    }

    private async Task RouteEmptyPublicApiAsync() =>
        await Page.RouteAsync("**/api/public/**", route => JsonAsync(route, Array.Empty<object>()));

    private async Task RouteEmptyAdminApiAsync() =>
        await Page.RouteAsync("**/api/admin/articles", route => JsonAsync(route, Array.Empty<object>()));

    private async Task RouteEditorsAsync(params EditorArticle[] articles)
    {
        await Page.RouteAsync("**/api/admin/**", route => HandleEditorRouteAsync(route, articles, includeTaxonomy: false));
        await Page.RouteAsync("**/api/public/articles/**", route => HandlePublicRouteAsync(route, articles));
    }

    private async Task RouteEditorAsync(EditorArticle article, bool includeTaxonomy = false)
    {
        await Page.RouteAsync("**/api/admin/**", route => HandleEditorRouteAsync(route, [article], includeTaxonomy));
        await Page.RouteAsync("**/api/public/articles/**", route => HandlePublicRouteAsync(route, [article]));
        await RouteEmptyPublicApiFallbackAsync();
    }

    private async Task RouteEditorOnContextAsync(EditorArticle article)
    {
        await Context.RouteAsync("**/api/admin/**", route => HandleEditorRouteAsync(route, [article], includeTaxonomy: false));
        await Context.RouteAsync("**/api/public/articles/**", route => HandlePublicRouteAsync(route, [article]));
    }

    private async Task RouteEmptyPublicApiFallbackAsync() =>
        await Page.RouteAsync("**/api/public/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.StartsWith("/api/public/articles/", StringComparison.Ordinal)) { await route.FallbackAsync(); return; }
            await JsonAsync(route, Array.Empty<object>());
        });

    private static async Task HandleEditorRouteAsync(IRoute route, EditorArticle[] articles, bool includeTaxonomy)
    {
        var path = new Uri(route.Request.Url).AbsolutePath;
        if (path == "/api/admin/taxonomy")
        {
            await JsonAsync(route, includeTaxonomy ? Taxonomy() : EmptyTaxonomy());
            return;
        }
        if (path == "/api/admin/markdown/preview")
        {
            using var request = JsonDocument.Parse(route.Request.PostData!);
            var markdown = WebUtility.HtmlEncode(request.RootElement.GetProperty("markdown").GetString());
            await JsonAsync(route, new { html = $"<p>{markdown}</p>" });
            return;
        }

        var article = articles.FirstOrDefault(candidate => path.StartsWith($"/api/admin/articles/{candidate.Id}", StringComparison.Ordinal));
        if (article is null) { await JsonAsync(route, new { }); return; }
        if (path == $"/api/admin/articles/{article.Id}" && route.Request.Method == "GET")
        {
            await JsonAsync(route, Details(article));
            return;
        }
        if (path.Contains("/localizations/", StringComparison.Ordinal) && route.Request.Method == "PUT")
        {
            using var request = JsonDocument.Parse(route.Request.PostData!);
            article.Title = request.RootElement.GetProperty("title").GetString() ?? "";
            article.Slug = request.RootElement.GetProperty("slug").GetString() ?? "";
            article.Markdown = request.RootElement.GetProperty("markdown").GetString() ?? "";
            article.Version++;
            await JsonAsync(route, article.Version);
            return;
        }
        await JsonAsync(route, new { });
    }

    private static async Task HandlePublicRouteAsync(IRoute route, EditorArticle[] articles)
    {
        var path = new Uri(route.Request.Url).AbsolutePath;
        if (path.EndsWith("/views", StringComparison.Ordinal)) { await JsonAsync(route, new { viewCount = 1L }); return; }
        var article = articles.FirstOrDefault(candidate => path.EndsWith($"/en/{candidate.Slug}", StringComparison.Ordinal)
            || path.EndsWith($"/en/articles/{candidate.Slug}", StringComparison.Ordinal));
        article ??= articles.FirstOrDefault(candidate => path.Contains($"/articles/{candidate.Slug}", StringComparison.Ordinal));
        if (article is null) { await route.FulfillAsync(new() { Status = 404 }); return; }
        await JsonAsync(route, PublicDetails(article));
    }

    private async Task DragSplitterAsync(ILocator splitter, double targetX)
    {
        var box = Assert.IsType<LocatorBoundingBoxResult>(await splitter.BoundingBoxAsync());
        await Page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)targetX, box.Y + box.Height / 2);
        await Page.Mouse.UpAsync();
    }

    private Task<bool> DispatchSaveShortcutAsync() => Page.EvaluateAsync<bool>("""
        () => !document.dispatchEvent(new KeyboardEvent('keydown', {
          key: 's', ctrlKey: true, bubbles: true, cancelable: true
        }))
        """);

    private static object Details(EditorArticle article) => new
    {
        id = article.Id, createdAt = DateTimeOffset.UtcNow.AddDays(-1), updatedAt = DateTimeOffset.UtcNow,
        localizations = new[]
        {
            Localization("en", article.Slug, article.Title, article.Markdown, article.Status, article.Version),
            Localization("ru", "chernovik-ru", "Русский черновик", "Русский текст", article.RussianStatus, 1L)
        },
        topicIds = Array.Empty<Guid>(), series = Array.Empty<object>(), tags = Array.Empty<string>()
    };

    private static object Localization(string language, string slug, string title, string markdown, int status, long version) => new
    {
        id = Guid.NewGuid(), version, languageCode = language, slug, title, summary = "Summary", markdown, status,
        publishedAt = status == 1 ? (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-1) : null,
        updatedAt = DateTimeOffset.UtcNow, lastEditedAt = DateTimeOffset.UtcNow
    };

    private static object PublicDetails(EditorArticle article) => new
    {
        languageCode = "en", slug = article.Slug, title = "Published article", summary = "Summary",
        html = $"<p>{WebUtility.HtmlEncode(article.Markdown)}</p>", publishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        updatedAt = DateTimeOffset.UtcNow, lastEditedAt = DateTimeOffset.UtcNow, authorDisplayName = "Part Four",
        availableLocalizations = new[] { new { languageCode = "en", url = $"/en/articles/{article.Slug}" } },
        topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<string>(), viewCount = 0L
    };

    private static object Taxonomy() => new
    {
        topics = new[] { new { id = Guid.NewGuid(), createdAt = DateTimeOffset.UtcNow, updatedAt = DateTimeOffset.UtcNow, localizations = new[] { new { id = Guid.NewGuid(), languageCode = "en", name = "Engineering", slug = "engineering", description = "" } } } },
        series = new[] { new { id = Guid.NewGuid(), createdAt = DateTimeOffset.UtcNow, updatedAt = DateTimeOffset.UtcNow, localizations = new[] { new { id = Guid.NewGuid(), languageCode = "en", title = "Series One", slug = "series-one", description = "" } }, articles = Array.Empty<object>() } },
        tags = Array.Empty<object>()
    };

    private static object EmptyTaxonomy() => new { topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<object>() };

    private static Task JsonAsync(IRoute route, object body, int status = 200) => route.FulfillAsync(new()
    {
        Status = status, ContentType = "application/json", Body = JsonSerializer.Serialize(body, body.GetType())
    });

    private sealed class EditorArticle
    {
        public Guid Id { get; } = Guid.NewGuid();
        public long Version { get; set; } = 1;
        public string Title { get; set; } = "Editor article";
        public string Slug { get; set; } = "editor-article";
        public string Markdown { get; set; } = "Original body";
        public int Status { get; set; }
        public int RussianStatus { get; set; }
    }
}
