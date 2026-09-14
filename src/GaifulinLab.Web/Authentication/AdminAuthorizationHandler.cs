using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;

namespace GaifulinLab.Web.Authentication;

public sealed class AdminAuthorizationHandler(
    AccessTokenStore tokenStore,
    TokenAuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigation,
    AccessTokenRefreshCoordinator? refreshCoordinator = null) : DelegatingHandler
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
            var token = refreshCoordinator is null
                ? AccessTokenRefreshCoordinator.RefreshResult.Available(await tokenStore.GetAsync() ?? string.Empty)
                : await refreshCoordinator.GetTokenForRequestAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(token.AccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            }

            var replay = await RequestReplay.CaptureAsync(request, cancellationToken);
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized || refreshCoordinator is null
                || token.Status != AccessTokenRefreshCoordinator.RefreshStatus.Available)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized
                    && (refreshCoordinator is null
                        || token.Status == AccessTokenRefreshCoordinator.RefreshStatus.SessionUnavailable))
                {
                    await EndSessionAsync();
                }

                return response;
            }

            var refreshed = await refreshCoordinator.RefreshAfterUnauthorizedAsync(token.AccessToken!, cancellationToken);
            if (refreshed.Status == AccessTokenRefreshCoordinator.RefreshStatus.SessionUnavailable)
            {
                await EndSessionAsync();
                return response;
            }

            if (refreshed.Status != AccessTokenRefreshCoordinator.RefreshStatus.Available || replay is null)
            {
                return response;
            }

            response.Dispose();
            await authenticationStateProvider.NotifySessionChangedAsync();
            using var retry = replay.Create();
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
            return await base.SendAsync(retry, cancellationToken);
        }
        else
        {
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private async Task EndSessionAsync()
    {
        await authenticationStateProvider.ClearTokenAsync();
        navigation.NavigateTo("/admin/login", replace: true);
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

    private sealed class RequestReplay(
        HttpMethod method,
        Uri? requestUri,
        Version version,
        HttpVersionPolicy versionPolicy,
        IReadOnlyDictionary<string, string[]> headers,
        byte[]? content,
        IReadOnlyDictionary<string, string[]>? contentHeaders)
    {
        private const int MaximumReplayContentBytes = 1_048_576;

        public static async Task<RequestReplay?> CaptureAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[]? content = null;
            IReadOnlyDictionary<string, string[]>? contentHeaders = null;
            if (request.Content is not null)
            {
                var mediaType = request.Content.Headers.ContentType?.MediaType;
                if (request.Content.Headers.ContentLength is > MaximumReplayContentBytes
                    || mediaType is not ("application/json" or "application/x-www-form-urlencoded"))
                {
                    return null;
                }

                try
                {
                    content = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
                {
                    return null;
                }

                if (content.Length > MaximumReplayContentBytes)
                {
                    return null;
                }

                contentHeaders = request.Content.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray());
            }

            return new RequestReplay(
                request.Method,
                request.RequestUri,
                request.Version,
                request.VersionPolicy,
                request.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray()),
                content,
                contentHeaders);
        }

        public HttpRequestMessage Create()
        {
            var request = new HttpRequestMessage(method, requestUri)
            {
                Version = version,
                VersionPolicy = versionPolicy
            };
            foreach (var (name, values) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, values);
            }

            if (content is not null)
            {
                request.Content = new ByteArrayContent(content);
                foreach (var (name, values) in contentHeaders!)
                {
                    request.Content.Headers.TryAddWithoutValidation(name, values);
                }
            }

            return request;
        }
    }
}
