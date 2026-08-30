using Microsoft.JSInterop;

namespace GaifulinLab.Web.Client.Authentication;

public sealed class AccessTokenStore(IJSRuntime jsRuntime)
{
    private const string StorageKey = "gaifulinlab.admin.access_token";

    public ValueTask<string?> GetAsync() =>
        jsRuntime.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);

    public ValueTask SetAsync(string accessToken) =>
        jsRuntime.InvokeVoidAsync("sessionStorage.setItem", StorageKey, accessToken);

    public ValueTask ClearAsync() =>
        jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
}
