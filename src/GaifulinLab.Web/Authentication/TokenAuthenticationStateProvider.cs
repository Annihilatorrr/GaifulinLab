using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace GaifulinLab.Web.Authentication;

public sealed class TokenAuthenticationStateProvider(AccessTokenStore tokenStore)
    : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal AnonymousUser = new(new ClaimsIdentity());

    public bool IsLoggedIn { get; private set; }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var accessToken = await tokenStore.GetAsync();
        var principal = CreatePrincipal(accessToken);
        IsLoggedIn = principal.Identity?.IsAuthenticated == true;

        if (!IsLoggedIn && accessToken is not null)
        {
            await tokenStore.ClearAsync();
        }

        return new AuthenticationState(principal);
    }

    public async Task SetTokenAsync(string accessToken)
    {
        await tokenStore.SetAsync(accessToken);
        var state = new AuthenticationState(CreatePrincipal(accessToken));
        IsLoggedIn = state.User.Identity?.IsAuthenticated == true;
        NotifyAuthenticationStateChanged(
            Task.FromResult(state));
    }

    public async Task ClearTokenAsync()
    {
        await tokenStore.ClearAsync();
        IsLoggedIn = false;
        NotifyAuthenticationStateChanged(
            Task.FromResult(new AuthenticationState(AnonymousUser)));
    }

    private static ClaimsPrincipal CreatePrincipal(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return AnonymousUser;
        }

        try
        {
            var segments = accessToken.Split('.');
            if (segments.Length != 3)
            {
                return AnonymousUser;
            }

            using var payload = JsonDocument.Parse(DecodeBase64Url(segments[1]));
            if (!payload.RootElement.TryGetProperty("exp", out var expirationElement)
                || !expirationElement.TryGetInt64(out var expirationSeconds)
                || DateTimeOffset.FromUnixTimeSeconds(expirationSeconds) <= DateTimeOffset.UtcNow)
            {
                return AnonymousUser;
            }

            var claims = new List<Claim>();
            AddClaim(payload.RootElement, claims, "sub", ClaimTypes.NameIdentifier);
            AddClaim(payload.RootElement, claims, "unique_name", ClaimTypes.Name);
            AddClaims(payload.RootElement, claims, "role", ClaimTypes.Role);

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return AnonymousUser;
        }
    }

    private static void AddClaims(
        JsonElement payload,
        ICollection<Claim> claims,
        string jsonName,
        string claimType)
    {
        if (!payload.TryGetProperty(jsonName, out var value))
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            claims.Add(new Claim(claimType, value.GetString()!));
            return;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String))
            {
                claims.Add(new Claim(claimType, item.GetString()!));
            }
        }
    }

    private static void AddClaim(
        JsonElement payload,
        ICollection<Claim> claims,
        string jsonName,
        string claimType)
    {
        if (payload.TryGetProperty(jsonName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            claims.Add(new Claim(claimType, value.GetString()!));
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64
        };

        return Convert.FromBase64String(base64);
    }
}
