using System.Net;
using System.Net.Http.Headers;
using GaifulinLab.Web.Authentication;
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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private sealed class TokenJsRuntime(string token) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult((TValue)(object)token);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://example.test/", "https://example.test/");
    }
}
