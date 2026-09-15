using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class LocalizationTests : E2EPageTest
{
    private static readonly Uri BaseUri = new(
        Environment.GetEnvironmentVariable("GAIFULINLAB_E2E_BASE_URL") ?? "http://localhost:5172");

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Context.AddInitScriptAsync(
            "if (!localStorage.getItem('GaifulinLab.Web.UiCulture')) "
            + "localStorage.setItem('GaifulinLab.Web.UiCulture', 'en');");
    }

    [Fact]
    public async Task LanguageSwitcher_UsesHorizontalPillBetweenSearchAndThemeToggle()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.RouteAsync("**/api/public/**", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "[]"
        }));
        await Page.GotoAsync(BaseUri.ToString());

        // The desktop header keeps the search action, language selector, and theme toggle in that order.
        var search = await Page.Locator(".search-link").BoundingBoxAsync()
            ?? throw new InvalidOperationException("The desktop search action was not rendered.");
        var language = await Page.GetByTestId("language-switcher").BoundingBoxAsync()
            ?? throw new InvalidOperationException("The language selector was not rendered.");
        var theme = await Page.Locator(".theme-toggle").BoundingBoxAsync()
            ?? throw new InvalidOperationException("The theme toggle was not rendered.");

        Assert.True(search.X < language.X);
        Assert.True(language.X < theme.X);

        // The selector is a pill aligned with the adjacent theme control rather than a round icon button.
        Assert.True(language.Width > language.Height);
        Assert.InRange(language.Width, 68, 76);
        Assert.InRange(Math.Abs(language.Height - theme.Height), 0, 1);
        await Expect(Page.GetByTestId("language-toggle").Locator("svg")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task SearchForm_UsesCompactRussianCopyAndAccessibleLabels()
    {
        await Page.RouteAsync("**/api/public/**", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = route.Request.Url.Contains("/search", StringComparison.Ordinal)
                ? "{\"items\":[],\"totalCount\":0,\"page\":1,\"pageSize\":10,\"totalPages\":0}"
                : "[]"
        }));
        await Page.GotoAsync(BaseUri.ToString());
        await Page.EvaluateAsync("localStorage.setItem('GaifulinLab.Web.UiCulture', 'ru')");
        await Page.GotoAsync(new Uri(BaseUri, "/search").ToString());

        // The visible Russian form contains only its controls, while the section and field remain named.
        var searchRegion = Page.GetByRole(AriaRole.Region, new() { Name = "Поиск", Exact = true });
        await Expect(searchRegion).ToBeVisibleAsync();
        await Expect(searchRegion.GetByRole(AriaRole.Searchbox, new() { Name = "Поиск статей", Exact = true })).ToBeVisibleAsync();
        await Expect(searchRegion.GetByRole(AriaRole.Button, new() { Name = "Найти", Exact = true })).ToBeVisibleAsync();
        await Expect(searchRegion.Locator(".tag-picker summary")).ToContainTextAsync("Все теги");
        await Expect(searchRegion.Locator(".search-filters > *")).ToHaveCountAsync(4);
        await Expect(Page.GetByText("Исследуйте лабораторию", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Найдите следующую идею.", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Ищите по темам, тегам, заголовкам и текстам статей.", new() { Exact = true })).ToHaveCountAsync(0);
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
