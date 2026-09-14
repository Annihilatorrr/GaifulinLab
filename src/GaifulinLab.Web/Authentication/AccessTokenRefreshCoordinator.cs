using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaifulinLab.Contracts.Auth;

namespace GaifulinLab.Web.Authentication;

/// <summary>
/// Serializes refreshes in one browser tab and keeps credentials after temporary server or network failures.
/// </summary>
public sealed class AccessTokenRefreshCoordinator(AccessTokenStore tokenStore, SessionRefreshClient refreshClient)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public async Task<RefreshResult> GetTokenForRequestAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return RefreshResult.Anonymous;
        }

        return GetExpiration(accessToken) is { } expiration && expiration > DateTimeOffset.UtcNow.Add(RefreshMargin)
            ? RefreshResult.Available(accessToken)
            : await RefreshAsync(accessToken, force: false, cancellationToken);
    }

    public Task<RefreshResult> RefreshAfterUnauthorizedAsync(string observedAccessToken, CancellationToken cancellationToken = default) =>
        RefreshAsync(observedAccessToken, force: true, cancellationToken);

    private async Task<RefreshResult> RefreshAsync(string observedAccessToken, bool force, CancellationToken cancellationToken)
    {
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            var currentAccessToken = await tokenStore.GetAsync();
            if (string.IsNullOrWhiteSpace(currentAccessToken))
            {
                return RefreshResult.SessionUnavailable;
            }

            if (force && !string.Equals(currentAccessToken, observedAccessToken, StringComparison.Ordinal))
            {
                return RefreshResult.Available(currentAccessToken);
            }

            if (!force && GetExpiration(currentAccessToken) is { } expiration
                && expiration > DateTimeOffset.UtcNow.Add(RefreshMargin))
            {
                return RefreshResult.Available(currentAccessToken);
            }

            var refreshToken = await tokenStore.GetRefreshTokenAsync();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                await tokenStore.ClearAsync();
                return RefreshResult.SessionUnavailable;
            }

            try
            {
                var response = await refreshClient.RefreshAsync(refreshToken, cancellationToken);
                if (response is null || string.IsNullOrWhiteSpace(response.AccessToken)
                    || string.IsNullOrWhiteSpace(response.RefreshToken))
                {
                    return RefreshResult.TransientFailure(currentAccessToken);
                }

                await tokenStore.SetSessionAsync(response.AccessToken, response.RefreshToken);
                return RefreshResult.Available(response.AccessToken);
            }
            catch (SessionRefreshRejectedException)
            {
                // Only an explicit client rejection invalidates the locally stored session.
                if (string.Equals(await tokenStore.GetRefreshTokenAsync(), refreshToken, StringComparison.Ordinal))
                {
                    await tokenStore.ClearAsync();
                }

                return RefreshResult.SessionUnavailable;
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                return RefreshResult.TransientFailure(currentAccessToken);
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    internal static DateTimeOffset? GetExpiration(string accessToken)
    {
        try
        {
            var segments = accessToken.Split('.');
            if (segments.Length != 3)
            {
                return null;
            }

            var base64 = segments[1].Replace('-', '+').Replace('_', '/');
            base64 = (base64.Length % 4) switch { 2 => base64 + "==", 3 => base64 + "=", _ => base64 };
            using var payload = JsonDocument.Parse(Convert.FromBase64String(base64));
            return payload.RootElement.TryGetProperty("exp", out var expiration)
                && expiration.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public enum RefreshStatus { Anonymous, Available, SessionUnavailable, TransientFailure }

    public readonly record struct RefreshResult(RefreshStatus Status, string? AccessToken)
    {
        public static readonly RefreshResult Anonymous = new(RefreshStatus.Anonymous, null);
        public static readonly RefreshResult SessionUnavailable = new(RefreshStatus.SessionUnavailable, null);
        public static RefreshResult Available(string accessToken) => new(RefreshStatus.Available, accessToken);
        public static RefreshResult TransientFailure(string accessToken) => new(RefreshStatus.TransientFailure, accessToken);
    }
}

public sealed class SessionRefreshClient(HttpClient httpClient)
{
    public async Task<LoginResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/auth/refresh", new RefreshTokenRequest(refreshToken), cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SessionRefreshRejectedException();
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/auth/logout", new LogoutRequest(refreshToken), cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class SessionRefreshRejectedException : Exception;
