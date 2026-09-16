using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class SessionRefreshTests(E2EEnvironment environment) : E2EPageTest
{
    private const string AccessTokenStorageKey = "gaifulinlab.admin.access_token";
    private const string RefreshTokenStorageKey = "gaifulinlab.admin.refresh_token";

    [Fact]
    public async Task ExpiredAccessToken_RefreshesAndSavesTheArticle()
    {
        var article = await SeedAndOpenEditorAsync();
        var originalRefreshToken = await GetStorageItemAsync(RefreshTokenStorageKey);
        var refreshRequests = 0;
        await Page.RouteAsync("**/api/auth/refresh", async route =>
        {
            if (route.Request.Method == "POST")
            {
                refreshRequests++;
            }

            await route.FallbackAsync();
        });
        await SetExpiredAccessTokenAsync();

        var updatedTitle = "Saved after session refresh " + Guid.NewGuid().ToString("N");
        await Page.GetByLabel("Article title").FillAsync(updatedTitle);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeDisabledAsync();

        Assert.Equal(1, refreshRequests);
        Assert.NotEqual(originalRefreshToken, await GetStorageItemAsync(RefreshTokenStorageKey));
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync(updatedTitle);
        Assert.Equal(article.Id.ToString(), new Uri(Page.Url).Segments[^1].Trim('/'));
    }

    [Fact]
    public async Task ConcurrentPreviewAndSave_UseOneRefreshAndPersistTheArticle()
    {
        var article = await SeedAndOpenEditorAsync();
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshRequests = 0;
        await Page.RouteAsync("**/api/auth/refresh", async route =>
        {
            if (route.Request.Method != "POST")
            {
                await route.FallbackAsync();
                return;
            }

            refreshRequests++;
            refreshStarted.TrySetResult();
            await releaseRefresh.Task;
            await route.FallbackAsync();
        });
        await SetExpiredAccessTokenAsync();

        var updatedHtml = "Concurrent refresh save " + Guid.NewGuid().ToString("N");
        await Page.GetByLabel("Article Html").FillAsync(updatedHtml);
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The preview request is waiting for refresh while Save starts a second protected request.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Saving…", Exact = true })).ToBeDisabledAsync();
        releaseRefresh.TrySetResult();

        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeDisabledAsync();
        Assert.Equal(1, refreshRequests);
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync(updatedHtml);
        Assert.Equal(article.Id.ToString(), new Uri(Page.Url).Segments[^1].Trim('/'));
    }

    [Fact]
    public async Task TemporaryRefreshFailure_KeepsTheSessionAndAllowsSaveRetry()
    {
        await SeedAndOpenEditorAsync();
        var originalRefreshToken = await GetStorageItemAsync(RefreshTokenStorageKey);
        await Page.RouteAsync("**/api/auth/refresh", route => FulfillRefreshFailureAsync(route, 503));
        await SetExpiredAccessTokenAsync();

        var updatedTitle = "Retry after unavailable refresh " + Guid.NewGuid().ToString("N");
        await Page.GetByLabel("Article title").FillAsync(updatedTitle);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();

        Assert.Contains("/admin/articles/", Page.Url, StringComparison.Ordinal);
        Assert.Equal(originalRefreshToken, await GetStorageItemAsync(RefreshTokenStorageKey));
        Assert.NotNull(await GetStorageItemAsync(AccessTokenStorageKey));

        await Page.UnrouteAsync("**/api/auth/refresh");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeDisabledAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync(updatedTitle);
    }

    [Fact]
    public async Task RejectedRefresh_ClearsSessionAndReturnsToSignIn()
    {
        await SeedAndOpenEditorAsync();
        await Page.RouteAsync("**/api/auth/refresh", route => FulfillRefreshFailureAsync(route, 401));
        await SetExpiredAccessTokenAsync();

        await Page.GetByLabel("Article title").FillAsync("This update must not be saved");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/admin/login$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
        Assert.Null(await GetStorageItemAsync(AccessTokenStorageKey));
        Assert.Null(await GetStorageItemAsync(RefreshTokenStorageKey));
    }

    private async Task<SeededArticle> SeedAndOpenEditorAsync()
    {
        var uniqueId = Guid.NewGuid().ToString("N");
        var article = await environment.SeedPublishedArticleAsync(
            "Session refresh " + uniqueId,
            "session-refresh-" + uniqueId,
            "Original article text.");
        var (login, password) = environment.GetAdminCredentials();
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My articles" })).ToBeVisibleAsync();

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync(article.Title);
        return article;
    }

    private async Task SetExpiredAccessTokenAsync() =>
        await Page.EvaluateAsync(
            "token => sessionStorage.setItem('gaifulinlab.admin.access_token', token)",
            CreateExpiredAccessToken());

    private async Task<string?> GetStorageItemAsync(string key) =>
        await Page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", key);

    private Task FulfillRefreshFailureAsync(IRoute route, int status)
    {
        var origin = environment.BaseUri.GetLeftPart(UriPartial.Authority);
        if (route.Request.Method == "OPTIONS")
        {
            return route.FulfillAsync(new()
            {
                Status = 204,
                Headers = new Dictionary<string, string>
                {
                    ["Access-Control-Allow-Origin"] = origin,
                    ["Access-Control-Allow-Methods"] = "POST",
                    ["Access-Control-Allow-Headers"] = "content-type"
                }
            });
        }

        return route.FulfillAsync(new()
        {
            Status = status,
            ContentType = "application/json",
            Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = origin },
            Body = "{\"code\":\"refresh_unavailable\",\"message\":\"The session could not be refreshed.\"}"
        });
    }

    private static string CreateExpiredAccessToken()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            sub = "e2e-session-refresh",
            role = "Admin",
            exp = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()
        })))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"header.{payload}.signature";
    }
}
