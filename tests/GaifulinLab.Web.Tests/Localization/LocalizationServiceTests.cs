using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaifulinLab.Web.Localization;
using Microsoft.JSInterop;

namespace GaifulinLab.Web.Tests.Localization;

public sealed class LocalizationServiceTests
{
    [Fact]
    public async Task InitializeAsync_UsesStoredCultureBeforeBrowserPreferenceAndAppliesHtmlLanguage()
    {
        var js = new StubJsRuntime(new() { ["GaifulinLab.uiCulture.get"] = "en", ["GaifulinLab.uiCulture.getPreferred"] = "ru" });
        var service = CreateService(js);

        await service.InitializeAsync();

        Assert.Equal("en", service.CurrentCode);
        Assert.Contains(js.Calls, call => call.Identifier == "GaifulinLab.uiCulture.apply" && Equals(call.Arguments[0], "en"));
    }

    [Fact]
    public async Task InitializeAsync_UsesSupportedBrowserLanguageThenRussianDefault()
    {
        var browserJs = new StubJsRuntime(new() { ["GaifulinLab.uiCulture.getPreferred"] = "en" });
        var browserService = CreateService(browserJs);
        await browserService.InitializeAsync();
        Assert.Equal("en", browserService.CurrentCode);

        var defaultService = CreateService(new StubJsRuntime(new() { ["GaifulinLab.uiCulture.getPreferred"] = "de" }));
        await defaultService.InitializeAsync();
        Assert.Equal("ru", defaultService.CurrentCode);
    }

    [Theory]
    [InlineData("english")]
    [InlineData("runner")]
    [InlineData("en/articles")]
    [InlineData("ru_articles")]
    [InlineData("en-")]
    public async Task InitializeAsync_IgnoresInvalidStoredCultureValues(string storedCulture)
    {
        var js = new StubJsRuntime(new()
        {
            ["GaifulinLab.uiCulture.get"] = storedCulture,
            ["GaifulinLab.uiCulture.getPreferred"] = "en"
        });
        var service = CreateService(js);

        await service.InitializeAsync();

        Assert.Equal("en", service.CurrentCode);
    }

    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("EN-gb", "en")]
    [InlineData("ru-RU", "ru")]
    public async Task InitializeAsync_AcceptsSupportedLocaleForms(string storedCulture, string expectedCode)
    {
        var service = CreateService(new StubJsRuntime(new()
        {
            ["GaifulinLab.uiCulture.get"] = storedCulture,
            ["GaifulinLab.uiCulture.getPreferred"] = expectedCode == "en" ? "ru" : "en"
        }));

        await service.InitializeAsync();

        Assert.Equal(expectedCode, service.CurrentCode);
    }

    [Theory]
    [InlineData("english")]
    [InlineData("runner")]
    [InlineData("ru/articles")]
    [InlineData("en?next=ru")]
    public async Task SynchronizeRouteCultureAsync_RejectsRouteLikeAndPrefixValues(string routeCulture)
    {
        var js = new StubJsRuntime(new() { ["GaifulinLab.uiCulture.get"] = "en" });
        var service = CreateService(js);
        await service.InitializeAsync();

        await service.SynchronizeRouteCultureAsync(routeCulture);

        Assert.Equal("en", service.CurrentCode);
        Assert.DoesNotContain(js.Calls, call => call.Identifier == "GaifulinLab.uiCulture.set");
    }

    [Fact]
    public async Task MissingRussianKey_FallsBackToEnglish()
    {
        var service = CreateService(new StubJsRuntime(), ruJson: "{}");
        await service.InitializeAsync();
        Assert.Equal("English value", service["test.key"]);
    }

    [Fact]
    public async Task FailedActiveBundle_CanBeRetriedWithoutChangingCulture()
    {
        var ruAttempts = 0;
        var js = new StubJsRuntime();
        var service = CreateService(js, request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("ru.json", StringComparison.Ordinal))
            {
                ruAttempts++;
                return ruAttempts == 1
                    ? new(HttpStatusCode.ServiceUnavailable)
                    : Json("{\"test.key\":\"Русское значение\"}");
            }
            return Json("{\"test.key\":\"English value\"}");
        });

        await service.InitializeAsync();
        Assert.Equal("English value", service["test.key"]);
        await service.SetCultureAsync(UiCulture.Ru);
        Assert.Equal("Русское значение", service["test.key"]);
        Assert.Equal(2, ruAttempts);
        Assert.Contains(js.Calls, call => call.Identifier == "GaifulinLab.uiCulture.navigateForCulture"
            && Equals(call.Arguments[0], "ru"));
    }

    [Fact]
    public async Task SetCultureAsync_PersistsSelectionAndNavigatesForCurrentRoute()
    {
        var js = new StubJsRuntime();
        var service = CreateService(js);
        await service.InitializeAsync();

        await service.SetCultureAsync(UiCulture.En);

        Assert.Contains(js.Calls, call => call.Identifier == "GaifulinLab.uiCulture.set"
            && Equals(call.Arguments[0], LocalizationService.StorageKey)
            && Equals(call.Arguments[1], "en"));
        Assert.Contains(js.Calls, call => call.Identifier == "GaifulinLab.uiCulture.navigateForCulture");
    }

    [Fact]
    public async Task RussianPluralHelpers_SelectOneFewAndManyForms()
    {
        var service = CreateService(new StubJsRuntime(), ruJson:
            "{\"common.articleCount.one\":\"{0} статья\",\"common.articleCount.few\":\"{0} статьи\",\"common.articleCount.many\":\"{0} статей\"}");
        await service.InitializeAsync();

        Assert.Equal("1 статья", service.FormatArticleCount(1));
        Assert.Equal("2 статьи", service.FormatArticleCount(2));
        Assert.Equal("11 статей", service.FormatArticleCount(11));
        var timestamp = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("02.01.2026", service.FormatDate(timestamp));
        Assert.Equal(
            $"{service.FormatDate(timestamp)}, {timestamp.ToLocalTime():HH:mm}",
            service.FormatDateTime(timestamp));
    }

    [Fact]
    public async Task ApiError_UsesStableCodeAndFallsBackForUnknownCode()
    {
        var service = CreateService(new StubJsRuntime(), ruJson:
            "{\"errors.api.login_taken\":\"Адрес уже используется.\",\"errors.generic\":\"Ошибка.\"}");
        await service.InitializeAsync();

        Assert.Equal("Адрес уже используется.", service.ApiError("login_taken"));
        Assert.Equal("Ошибка.", service.ApiError("unknown"));
    }

    [Fact]
    public void TranslationBundles_HaveIdenticalNonEmptyKeySets()
    {
        var root = FindRepositoryRoot();
        var en = ReadBundle(Path.Combine(root, "src", "GaifulinLab.Web", "wwwroot", "i18n", "en.json"));
        var ru = ReadBundle(Path.Combine(root, "src", "GaifulinLab.Web", "wwwroot", "i18n", "ru.json"));

        Assert.Equal(en.Keys.Order(), ru.Keys.Order());
        Assert.DoesNotContain(en, pair => string.IsNullOrWhiteSpace(pair.Value));
        Assert.DoesNotContain(ru, pair => string.IsNullOrWhiteSpace(pair.Value));
    }

    [Fact]
    public void TranslationBundle_ContainsEveryLiteralComponentKey()
    {
        var root = FindRepositoryRoot();
        var en = ReadBundle(Path.Combine(root, "src", "GaifulinLab.Web", "wwwroot", "i18n", "en.json"));
        var components = Path.Combine(root, "src", "GaifulinLab.Web", "Components");
        var keys = Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories)
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"L(?:\[""([^""]+)""\]|\.Format\(""([^""]+)"")")
                .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal);

        Assert.DoesNotContain(keys, key => !en.ContainsKey(key));
    }

    private static LocalizationService CreateService(StubJsRuntime js, string ruJson = "{\"test.key\":\"Русское значение\"}") =>
        CreateService(js, request => request.RequestUri!.AbsolutePath.EndsWith("ru.json", StringComparison.Ordinal)
            ? Json(ruJson)
            : Json("{\"test.key\":\"English value\"}"));

    private static LocalizationService CreateService(
        StubJsRuntime js,
        Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(js, new HttpClient(new StubHttpMessageHandler(handler)) { BaseAddress = new Uri("http://web/") });

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private static Dictionary<string, string> ReadBundle(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GaifulinLab.slnx"))) return directory.FullName;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class StubJsRuntime(Dictionary<string, object?>? values = null) : IJSRuntime
    {
        public List<(string Identifier, object?[] Arguments)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var arguments = args ?? [];
            Calls.Add((identifier, arguments));
            var result = values is not null && values.TryGetValue(identifier, out var value) ? value : null;
            return ValueTask.FromResult(result is TValue typed ? typed : default!);
        }
    }
}
