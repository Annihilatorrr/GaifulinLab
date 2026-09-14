using Microsoft.JSInterop;

namespace GaifulinLab.Web.Authentication;

public sealed class AccessTokenStore(IJSRuntime jsRuntime)
{
    private const string AccessTokenStorageKey = "gaifulinlab.admin.access_token";
    private const string RefreshTokenStorageKey = "gaifulinlab.admin.refresh_token";

    public ValueTask<string?> GetAsync() =>
        jsRuntime.InvokeAsync<string?>("sessionStorage.getItem", AccessTokenStorageKey);

    public ValueTask<string?> GetRefreshTokenAsync() =>
        jsRuntime.InvokeAsync<string?>("sessionStorage.getItem", RefreshTokenStorageKey);

    public ValueTask SetAsync(string accessToken) =>
        jsRuntime.InvokeVoidAsync("sessionStorage.setItem", AccessTokenStorageKey, accessToken);

    public async ValueTask SetSessionAsync(string accessToken, string refreshToken)
    {
        await jsRuntime.InvokeVoidAsync("sessionStorage.setItem", AccessTokenStorageKey, accessToken);
        await jsRuntime.InvokeVoidAsync("sessionStorage.setItem", RefreshTokenStorageKey, refreshToken);
    }

    public async ValueTask ClearAsync()
    {
        await jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", AccessTokenStorageKey);
        await jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", RefreshTokenStorageKey);
    }
}
