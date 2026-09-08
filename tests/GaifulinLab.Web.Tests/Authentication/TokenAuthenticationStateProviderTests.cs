using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GaifulinLab.Web.Authentication;
using Microsoft.JSInterop;

namespace GaifulinLab.Web.Tests.Authentication;

public sealed class TokenAuthenticationStateProviderTests
{
    [Fact]
    public async Task GetAuthenticationStateAsync_RecognizesRoleFromStandardJwtClaim()
    {
        var token = CreateToken(new Dictionary<string, object?>
        {
            ["sub"] = "admin-id",
            [ClaimTypes.Role] = "Admin",
            ["exp"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
        });
        var provider = new TokenAuthenticationStateProvider(
            new AccessTokenStore(new TokenJsRuntime(token)));

        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.IsInRole("Admin"));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("header.eyJleHAiOjF9.signature")]
    public async Task GetAuthenticationStateAsync_ClearsMalformedOrExpiredTokens(string token)
    {
        var js = new TokenJsRuntime(token);
        var provider = new TokenAuthenticationStateProvider(new AccessTokenStore(js));

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
        Assert.False(provider.IsLoggedIn);
        Assert.Null(js.Token);
        Assert.Equal(1, js.ClearCount);
    }

    private static string CreateToken(IReadOnlyDictionary<string, object?> payload) =>
        $"header.{Encode(JsonSerializer.Serialize(payload))}.signature";

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class TokenJsRuntime(string? token) : IJSRuntime
    {
        public string? Token { get; private set; } = token;
        public int ClearCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier == "sessionStorage.getItem")
            {
                return new((TValue)(object?)Token!);
            }

            if (identifier == "sessionStorage.removeItem")
            {
                Token = null;
                ClearCount++;
            }

            return new(default(TValue)!);
        }
    }
}
