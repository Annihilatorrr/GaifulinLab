using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;

namespace GaifulinLab.Web.Client.Authentication;

public sealed class AdminAuthorizationHandler(
    AccessTokenStore tokenStore,
    TokenAuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var isProtectedRequest = request.RequestUri?.AbsolutePath is { } path
            && (path.Equals("/api/admin", StringComparison.Ordinal)
                || path.StartsWith("/api/admin/", StringComparison.Ordinal)
                || path.Equals("/api/auth/session", StringComparison.Ordinal));

        if (isProtectedRequest)
        {
            var accessToken = await tokenStore.GetAsync();
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (isProtectedRequest && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await authenticationStateProvider.ClearTokenAsync();
            navigation.NavigateTo("/admin/login", replace: true);
        }

        return response;
    }
}
