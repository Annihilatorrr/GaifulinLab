using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GaifulinLab.Web.Authentication;
using GaifulinLab.Contracts.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace GaifulinLab.Web.Tests.Authentication;

public sealed class AdminAuthorizationHandlerTests
{
    [Fact]
    public async Task ReaderPdfDownload_AttachesBearerTokenWithoutBroadeningPublicApiAuthentication()
    {
        var tokenStore = new AccessTokenStore(new TokenJsRuntime("reader-token"));
        var navigation = new TestNavigationManager();
        var authenticationState = new TokenAuthenticationStateProvider(tokenStore);
        var requests = new List<HttpRequestMessage>();
        using var handler = new AdminAuthorizationHandler(tokenStore, authenticationState, navigation)
        {
            InnerHandler = new StubHandler(request =>
            {
                requests.Add(request);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            })
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        await client.GetAsync("/api/public/articles/en/complex-numbers/pdf");
        await client.GetAsync("/api/public/articles/en/complex-numbers");

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "reader-token"), requests[0].Headers.Authorization);
        Assert.Null(requests[1].Headers.Authorization);
    }

    [Fact]
    public async Task ProtectedRequest_AfterUnauthorized_RefreshesAndReplaysExactlyOnce()
    {
        var oldToken = CreateToken(DateTimeOffset.UtcNow.AddMinutes(5));
        var newToken = CreateToken(DateTimeOffset.UtcNow.AddMinutes(5));
        var js = new TokenJsRuntime(oldToken, "refresh-token");
        var store = new AccessTokenStore(js);
        var refreshClient = new SessionRefreshClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new LoginResponse(newToken, DateTimeOffset.UtcNow.AddMinutes(5), "replacement-refresh-token"))
        })) { BaseAddress = new Uri("https://example.test/") });
        var coordinator = new AccessTokenRefreshCoordinator(store, refreshClient);
        var navigation = new TestNavigationManager();
        var authenticationState = new TokenAuthenticationStateProvider(store, coordinator);
        var authorizationHeaders = new List<AuthenticationHeaderValue?>();
        using var handler = new AdminAuthorizationHandler(store, authenticationState, navigation, coordinator)
        {
            InnerHandler = new StubHandler(request =>
            {
                authorizationHeaders.Add(request.Headers.Authorization);
                return new HttpResponseMessage(authorizationHeaders.Count == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
            })
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var response = await client.GetAsync("/api/admin/articles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, authorizationHeaders.Count);
        Assert.Equal(oldToken, authorizationHeaders[0]?.Parameter);
        Assert.Equal(newToken, authorizationHeaders[1]?.Parameter);
        Assert.Equal("replacement-refresh-token", js.RefreshToken);
    }

    [Fact]
    public async Task ProtectedRequest_WhenRefreshIsTemporarilyUnavailable_PreservesTheSession()
    {
        var expiredToken = CreateToken(DateTimeOffset.UtcNow.AddMinutes(-1));
        var js = new TokenJsRuntime(expiredToken, "refresh-token");
        var store = new AccessTokenStore(js);
        var refreshClient = new SessionRefreshClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
        {
            BaseAddress = new Uri("https://example.test/")
        });
        var coordinator = new AccessTokenRefreshCoordinator(store, refreshClient);
        var authenticationState = new TokenAuthenticationStateProvider(store, coordinator);
        using var handler = new AdminAuthorizationHandler(store, authenticationState, new TestNavigationManager(), coordinator)
        {
            InnerHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var response = await client.GetAsync("/api/admin/articles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(expiredToken, js.AccessToken);
        Assert.Equal("refresh-token", js.RefreshToken);
    }

    [Fact]
    public async Task ExpiredAccessToken_IsRefreshedBeforeTheProtectedRequest()
    {
        var newToken = CreateToken(DateTimeOffset.UtcNow.AddMinutes(5));
        var js = new TokenJsRuntime(CreateToken(DateTimeOffset.UtcNow.AddMinutes(-1)), "refresh-token");
        var store = new AccessTokenStore(js);
        var refreshClient = new SessionRefreshClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new LoginResponse(newToken, DateTimeOffset.UtcNow.AddMinutes(5), "replacement-refresh-token"))
        })) { BaseAddress = new Uri("https://example.test/") });
        var coordinator = new AccessTokenRefreshCoordinator(store, refreshClient);
        var authenticationState = new TokenAuthenticationStateProvider(store, coordinator);
        AuthenticationHeaderValue? observedAuthorization = null;
        using var handler = new AdminAuthorizationHandler(store, authenticationState, new TestNavigationManager(), coordinator)
        {
            InnerHandler = new StubHandler(request =>
            {
                observedAuthorization = request.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK);
            })
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var response = await client.GetAsync("/api/admin/articles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(newToken, observedAuthorization?.Parameter);
    }

    [Fact]
    public async Task ProtectedRequest_WhenRefreshIsRejected_ClearsTheSession()
    {
        var js = new TokenJsRuntime(CreateToken(DateTimeOffset.UtcNow.AddMinutes(-1)), "refresh-token");
        var store = new AccessTokenStore(js);
        var refreshClient = new SessionRefreshClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)))
        {
            BaseAddress = new Uri("https://example.test/")
        });
        var coordinator = new AccessTokenRefreshCoordinator(store, refreshClient);
        var authenticationState = new TokenAuthenticationStateProvider(store, coordinator);
        using var handler = new AdminAuthorizationHandler(store, authenticationState, new TestNavigationManager(), coordinator)
        {
            InnerHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var response = await client.GetAsync("/api/admin/articles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(js.AccessToken);
        Assert.Null(js.RefreshToken);
    }

    private static string CreateToken(DateTimeOffset expiresAt) =>
        $"header.{Encode(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["sub"] = "admin-id",
            [ClaimTypes.Role] = "Admin",
            ["exp"] = expiresAt.ToUnixTimeSeconds()
        }))}.signature";

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private sealed class TokenJsRuntime(string? accessToken, string? refreshToken = null) : IJSRuntime
    {
        public string? AccessToken { get; private set; } = accessToken;
        public string? RefreshToken { get; private set; } = refreshToken;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            var key = (string?)args?.FirstOrDefault();
            if (identifier == "sessionStorage.getItem")
            {
                return new((TValue)(object?)(key == "gaifulinlab.admin.access_token" ? AccessToken : RefreshToken)!);
            }

            if (identifier == "sessionStorage.setItem")
            {
                if (key == "gaifulinlab.admin.access_token") AccessToken = (string?)args?[1];
                if (key == "gaifulinlab.admin.refresh_token") RefreshToken = (string?)args?[1];
            }

            if (identifier == "sessionStorage.removeItem")
            {
                if (key == "gaifulinlab.admin.access_token") AccessToken = null;
                if (key == "gaifulinlab.admin.refresh_token") RefreshToken = null;
            }

            return new(default(TValue)!);
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://example.test/", "https://example.test/");

        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }
}
