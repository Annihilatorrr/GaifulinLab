using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace GaifulinLab.Web.Localization;

public sealed class LocalizationService(IJSRuntime jsRuntime, HttpClient webClient)
{
    internal const string StorageKey = "GaifulinLab.Web.UiCulture";
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private readonly Dictionary<UiCulture, IReadOnlyDictionary<string, string>> _dictionaries = [];
    private readonly HashSet<UiCulture> _failedLoads = [];
    private bool _initialized;
    private CultureInfo _formatCulture = RussianCulture;

    public UiCulture CurrentCulture { get; private set; } = UiCulture.Ru;
    public string CurrentCode => CurrentCulture == UiCulture.En ? "en" : "ru";
    public string this[string key] => Get(key);

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        (CurrentCulture, _formatCulture) = await ResolveInitialCultureAsync();
        await EnsureLoadedAsync(UiCulture.En);
        await EnsureLoadedAsync(CurrentCulture);
        ApplyDotNetCulture();
        await InvokeJsAsync("GaifulinLab.uiCulture.apply", CurrentCode);
    }

    public async Task SetCultureAsync(UiCulture culture)
    {
        if (!_initialized)
        {
            await InitializeAsync();
        }

        if (CurrentCulture == culture)
        {
            if (_failedLoads.Contains(culture))
            {
                await EnsureLoadedAsync(culture, forceReload: true);
                if (!_failedLoads.Contains(culture))
                {
                    ApplyDotNetCulture();
                    await InvokeJsAsync("GaifulinLab.uiCulture.set", StorageKey, CurrentCode);
                    await InvokeJsAsync("GaifulinLab.uiCulture.navigateForCulture", CurrentCode);
                }
            }
            return;
        }

        CurrentCulture = culture;
        _formatCulture = await ResolveFormatCultureAsync(culture);
        await EnsureLoadedAsync(culture);
        ApplyDotNetCulture();
        await InvokeJsAsync("GaifulinLab.uiCulture.set", StorageKey, CurrentCode);
        await InvokeJsAsync("GaifulinLab.uiCulture.navigateForCulture", CurrentCode);
    }

    public async Task SynchronizeRouteCultureAsync(string? code)
    {
        if (!TryParse(code, out var culture) || culture == CurrentCulture)
        {
            return;
        }

        CurrentCulture = culture;
        _formatCulture = await ResolveFormatCultureAsync(culture, code);
        await EnsureLoadedAsync(culture);
        ApplyDotNetCulture();
        await InvokeJsAsync("GaifulinLab.uiCulture.set", StorageKey, CurrentCode);
    }

    public string Get(string key)
    {
        if (TryGet(CurrentCulture, key, out var value)
            || CurrentCulture != UiCulture.En && TryGet(UiCulture.En, key, out value))
        {
            return value;
        }

        return key;
    }

    public string Format(string key, params object[] arguments) =>
        string.Format(_formatCulture, Get(key), arguments);

    public string FormatDate(DateTimeOffset value) => value.ToLocalTime().ToString(
        CurrentCulture == UiCulture.Ru ? "dd.MM.yyyy" : "d MMM yyyy",
        _formatCulture);

    public string FormatDateTime(DateTimeOffset value) => value.ToLocalTime().ToString(
        CurrentCulture == UiCulture.Ru ? "dd.MM.yyyy, HH:mm" : "d MMM yyyy, HH:mm",
        _formatCulture);

    public string FormatArticleCount(long value)
    {
        if (CurrentCulture == UiCulture.En)
        {
            return Format(value == 1 ? "common.articleCount.one" : "common.articleCount.many", value);
        }

        var absolute = Math.Abs(value);
        var modulo100 = absolute % 100;
        var modulo10 = absolute % 10;
        var key = modulo100 is >= 11 and <= 14
            ? "common.articleCount.many"
            : modulo10 == 1
                ? "common.articleCount.one"
                : modulo10 is >= 2 and <= 4
                    ? "common.articleCount.few"
                    : "common.articleCount.many";
        return Format(key, value);
    }

    public string FormatViewCount(long value)
    {
        if (CurrentCulture == UiCulture.En)
        {
            return Format(value == 1 ? "common.viewCount.one" : "common.viewCount.many", value);
        }

        var absolute = Math.Abs(value);
        var modulo100 = absolute % 100;
        var modulo10 = absolute % 10;
        var key = modulo100 is >= 11 and <= 14
            ? "common.viewCount.many"
            : modulo10 == 1
                ? "common.viewCount.one"
                : modulo10 is >= 2 and <= 4
                    ? "common.viewCount.few"
                    : "common.viewCount.many";
        return Format(key, value);
    }

    public string ApiError(string? code, string fallbackKey = "errors.generic") =>
        !string.IsNullOrWhiteSpace(code) && Get($"errors.api.{code}") is var translated
            && translated != $"errors.api.{code}"
                ? translated
                : Get(fallbackKey);

    private async Task<(UiCulture Culture, CultureInfo FormatCulture)> ResolveInitialCultureAsync()
    {
        try
        {
            var stored = await jsRuntime.InvokeAsync<string?>("GaifulinLab.uiCulture.get", StorageKey);
            if (TryParse(stored, out var storedCulture))
            {
                return (storedCulture, await ResolveFormatCultureAsync(storedCulture, stored));
            }

            var preferred = await jsRuntime.InvokeAsync<string?>("GaifulinLab.uiCulture.getPreferred");
            if (TryParse(preferred, out var preferredCulture))
            {
                return (preferredCulture, await ResolveFormatCultureAsync(preferredCulture));
            }
        }
        catch (JSException)
        {
        }

        return (UiCulture.Ru, RussianCulture);
    }

    private async Task EnsureLoadedAsync(UiCulture culture, bool forceReload = false)
    {
        if (!forceReload && _dictionaries.ContainsKey(culture) && !_failedLoads.Contains(culture))
        {
            return;
        }

        try
        {
            var code = culture == UiCulture.En ? "en" : "ru";
            var values = await webClient.GetFromJsonAsync<Dictionary<string, string>>($"i18n/{code}.json");
            _dictionaries[culture] = new ReadOnlyDictionary<string, string>(
                values?.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal));
            _failedLoads.Remove(culture);
        }
        catch (Exception exception) when (exception is HttpRequestException or NotSupportedException or JsonException)
        {
            _dictionaries.Remove(culture);
            _failedLoads.Add(culture);
            Console.Error.WriteLine($"Failed to load localization bundle '{culture}': {exception.Message}");
        }
    }

    private async Task<CultureInfo> ResolveFormatCultureAsync(UiCulture culture, string? candidate = null)
    {
        if (TryCreateFormatCulture(culture, candidate, out var result))
        {
            return result;
        }

        try
        {
            candidate = await jsRuntime.InvokeAsync<string?>("GaifulinLab.uiCulture.getPreferredLocale");
            if (TryCreateFormatCulture(culture, candidate, out result))
            {
                return result;
            }
        }
        catch (JSException)
        {
        }

        return culture == UiCulture.En ? EnglishCulture : RussianCulture;
    }

    private static bool TryCreateFormatCulture(UiCulture culture, string? code, out CultureInfo result)
    {
        result = culture == UiCulture.En ? EnglishCulture : RussianCulture;
        if (string.IsNullOrWhiteSpace(code) || code.Length <= 2
            || !code.StartsWith(culture == UiCulture.En ? "en" : "ru", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            result = CultureInfo.GetCultureInfo(code.Replace('_', '-'));
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool TryParse(string? code, out UiCulture culture)
    {
        culture = UiCulture.Ru;
        if (string.Equals(code, "en", StringComparison.OrdinalIgnoreCase))
        {
            culture = UiCulture.En;
            return true;
        }
        if (string.Equals(code, "ru", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(code)
            || code.Length <= 3
            || code[2] != '-'
            || !(code.StartsWith("en-", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("ru-", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        try
        {
            var locale = CultureInfo.GetCultureInfo(code);
            culture = string.Equals(locale.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase)
                ? UiCulture.En
                : UiCulture.Ru;
            return string.Equals(locale.TwoLetterISOLanguageName, culture == UiCulture.En ? "en" : "ru", StringComparison.OrdinalIgnoreCase);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private bool TryGet(UiCulture culture, string key, out string value)
    {
        if (_dictionaries.TryGetValue(culture, out var dictionary)
            && dictionary.TryGetValue(key, out var localized))
        {
            value = localized;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private void ApplyDotNetCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = _formatCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _formatCulture;
        CultureInfo.CurrentCulture = _formatCulture;
        CultureInfo.CurrentUICulture = _formatCulture;
    }

    private async Task InvokeJsAsync(string identifier, params object?[] arguments)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(identifier, arguments);
        }
        catch (JSException)
        {
        }
    }
}
