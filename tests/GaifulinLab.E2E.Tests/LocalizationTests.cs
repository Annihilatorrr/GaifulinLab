using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

public sealed class LocalizationTests : PageTest
{
    private static readonly Uri BaseUri = new(
        Environment.GetEnvironmentVariable("GAIFULINLAB_E2E_BASE_URL") ?? "http://localhost:5172");

    public override BrowserNewContextOptions ContextOptions() => new() { Locale = "en-US" };

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Context.AddInitScriptAsync(
            "if (!localStorage.getItem('GaifulinLab.Web.UiCulture')) "
            + "localStorage.setItem('GaifulinLab.Web.UiCulture', 'en');");
    }

    [Fact]
    public async Task GlobalLanguageSwitcher_ChangesContentLanguageAndPersistsAcrossReload()
    {
        var requestedUrls = new List<string>();
        await Page.RouteAsync("**/api/public/**", async route =>
        {
            requestedUrls.Add(route.Request.Url);
            var isRussianArticleList = route.Request.Url.Contains("/api/public/articles?", StringComparison.Ordinal)
                && route.Request.Url.Contains("languageCode=ru", StringComparison.Ordinal);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = isRussianArticleList
                    ? "[{\"languageCode\":\"ru\",\"slug\":\"tolko-russkaya\",\"title\":\"Только русская статья\",\"summary\":\"\",\"publishedAt\":\"2026-01-01T00:00:00Z\",\"authorDisplayName\":\"Автор\",\"topics\":[],\"series\":[],\"tags\":[],\"readingMinutes\":1}]"
                    : "[]"
            });
        });
        await Page.GotoAsync(BaseUri.ToString());

        // The global selector controls both translated UI and all public content requests.
        await Page.GetByTestId("language-toggle").ClickAsync();
        await Page.GetByTestId("language-ru").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Инженерные заметки, эксперименты и подробные разборы." })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Только русская статья", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Equal("ru", await Page.EvaluateAsync<string>("document.documentElement.lang"));
        Assert.Equal("ru", await Page.EvaluateAsync<string>("localStorage.getItem('GaifulinLab.Web.UiCulture')"));
        Assert.Contains(requestedUrls, url => url.Contains("languageCode=ru", StringComparison.Ordinal));
        Assert.Contains(requestedUrls, url => url.Contains("/topics/ru", StringComparison.Ordinal));

        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("language-current")).ToHaveTextAsync("RU");
    }

    [Fact]
    public async Task LanguageSwitcher_ClosesOnOutsideClickAndSearchDropsLanguageBoundFilters()
    {
        await Page.SetViewportSizeAsync(375, 780);
        await Page.RouteAsync("**/api/public/**", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = route.Request.Url.Contains("/search", StringComparison.Ordinal)
                ? "{\"items\":[],\"totalCount\":0,\"page\":1,\"pageSize\":10,\"totalPages\":0}"
                : "[]"
        }));
        await Page.GotoAsync(new Uri(BaseUri, "/search?q=dsp&topic=old&tag=old&page=3").ToString());

        var switcher = Page.GetByTestId("language-switcher");
        await Page.GetByTestId("language-toggle").ClickAsync();
        await Expect(switcher).ToHaveAttributeAsync("open", "");
        await Page.Locator("main").DispatchEventAsync("pointerdown");
        await Expect(switcher).Not.ToHaveAttributeAsync("open", "");

        await Page.GetByTestId("language-toggle").ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(switcher).Not.ToHaveAttributeAsync("open", "");
        await Expect(Page.GetByTestId("language-toggle")).ToBeFocusedAsync();

        await Page.GetByTestId("language-toggle").ClickAsync();
        var currentLanguage = await Page.GetByTestId("language-current").TextContentAsync();
        await Page.GetByTestId(currentLanguage == "RU" ? "language-en" : "language-ru").ClickAsync();
        await Page.WaitForURLAsync(url => !url.Contains("topic=", StringComparison.Ordinal));
        Assert.DoesNotContain("topic=", Page.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("tag=", Page.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("page=", Page.Url, StringComparison.Ordinal);
        Assert.Contains("q=dsp", Page.Url, StringComparison.Ordinal);

        var hasHorizontalOverflow = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.False(hasHorizontalOverflow);
    }

    [Fact]
    public async Task RegistrationValidation_UsesSelectedInterfaceLanguage()
    {
        await Page.RouteAsync("**/api/public/**", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "[]"
        }));
        await Page.GotoAsync(BaseUri.ToString());
        await Page.EvaluateAsync("localStorage.setItem('GaifulinLab.Web.UiCulture', 'ru')");
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("language-current")).ToHaveTextAsync("RU");

        await Page.GotoAsync(new Uri(BaseUri, "/register").ToString());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Создать аккаунт" }).ClickAsync();

        await Expect(Page.GetByText("Укажите отображаемое имя.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Укажите адрес электронной почты.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Укажите пароль.", new() { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ArticleRoute_IsAuthoritativeAndSwitcherUsesAvailableLocalizationUrl()
    {
        await Page.RouteAsync("**/api/public/articles/**", async route =>
        {
            if (route.Request.Method == "POST")
            {
                await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{\"viewCount\":1}" });
                return;
            }

            var russian = route.Request.Url.Contains("/articles/ru/", StringComparison.Ordinal);
            var body = $$"""
                {"languageCode":"{{(russian ? "ru" : "en")}}","slug":"{{(russian ? "russkaya" : "english")}}","title":"{{(russian ? "Русская версия" : "English version")}}","summary":"","html":"<p>Body</p>","publishedAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z","lastEditedAt":"2026-01-01T00:00:00Z","authorDisplayName":"Author","availableLocalizations":[{"languageCode":"en","url":"/en/articles/english"},{"languageCode":"ru","url":"/ru/articles/russkaya"}],"topics":[],"series":[],"tags":[],"viewCount":0}
                """;
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = body });
        });

        await Page.GotoAsync(new Uri(BaseUri, "/en/articles/english").ToString());
        await Expect(Page.GetByTestId("language-current")).ToHaveTextAsync("EN");
        await Page.GetByTestId("language-toggle").ClickAsync();
        await Page.GetByTestId("language-ru").ClickAsync();
        await Page.WaitForURLAsync(url => url.EndsWith("/ru/articles/russkaya", StringComparison.Ordinal));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Русская версия" })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("language-current")).ToHaveTextAsync("RU");
    }

    [Fact]
    public async Task DetailLanguageSwitch_FallsBackToTheMatchingCatalogWithoutATranslation()
    {
        await Page.RouteAsync("**/api/public/**", async route =>
        {
            if (route.Request.Method == "POST")
            {
                await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{\"viewCount\":1}" });
                return;
            }

            if (route.Request.Url.Contains("/api/public/articles/en/only-english", StringComparison.Ordinal))
            {
                await route.FulfillAsync(new()
                {
                    Status = 200,
                    ContentType = "application/json",
                    Body = "{\"languageCode\":\"en\",\"slug\":\"only-english\",\"title\":\"Only English\",\"summary\":\"\",\"html\":\"<p>Body</p>\",\"publishedAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\",\"lastEditedAt\":\"2026-01-01T00:00:00Z\",\"authorDisplayName\":\"Author\",\"availableLocalizations\":[{\"languageCode\":\"en\",\"url\":\"/en/articles/only-english\"}],\"topics\":[],\"series\":[],\"tags\":[],\"viewCount\":0}"
                });
                return;
            }

            if (route.Request.Url.Contains("/api/public/series/en/only-english", StringComparison.Ordinal))
            {
                await route.FulfillAsync(new()
                {
                    Status = 200,
                    ContentType = "application/json",
                    Body = "{\"languageCode\":\"en\",\"slug\":\"only-english\",\"title\":\"Only English series\",\"description\":null,\"articles\":[]}"
                });
                return;
            }

            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "[]" });
        });

        await Page.GotoAsync(new Uri(BaseUri, "/en/articles/only-english").ToString());
        await Page.GetByTestId("language-toggle").ClickAsync();
        await Page.GetByTestId("language-ru").ClickAsync();
        await Page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/articles");

        await Page.GotoAsync(new Uri(BaseUri, "/en/series/only-english").ToString());
        await Expect(Page.GetByTestId("language-current")).ToHaveTextAsync("EN");
        await Page.GetByTestId("language-toggle").ClickAsync();
        await Page.GetByTestId("language-ru").ClickAsync();
        await Page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/series");
    }
}
