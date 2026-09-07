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

    private static string CreateToken(IReadOnlyDictionary<string, object?> payload) =>
        $"header.{Encode(JsonSerializer.Serialize(payload))}.signature";

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class TokenJsRuntime(string token) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            new((TValue)(object)token);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            new((TValue)(object)token);
    }
}
