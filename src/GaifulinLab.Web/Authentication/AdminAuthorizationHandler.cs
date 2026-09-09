using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;

namespace GaifulinLab.Web.Authentication;

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
                || path.Equals("/api/auth/session", StringComparison.Ordinal)
                || path.Equals("/api/auth/profile", StringComparison.Ordinal)
                || IsReaderPdfDownloadPath(path));

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

    private static bool IsReaderPdfDownloadPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 6
            && string.Equals(segments[0], "api", StringComparison.Ordinal)
            && string.Equals(segments[1], "public", StringComparison.Ordinal)
            && string.Equals(segments[2], "articles", StringComparison.Ordinal)
            && string.Equals(segments[5], "pdf", StringComparison.Ordinal);
    }
}
