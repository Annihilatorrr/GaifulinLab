using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Content;
using GaifulinLab.Web.Tests.Authentication;

namespace GaifulinLab.Web.Tests.Content;

public sealed class AdminMarkdownEndpointsTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Preview_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/admin/markdown/preview",
            new MarkdownPreviewRequest("# Preview"));

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected 401, received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Preview_WithBearerToken_ReturnsSanitizedHtml()
    {
        using var client = await CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/api/admin/markdown/preview",
            new MarkdownPreviewRequest(
                "# Preview\n\n**Safe** <img src=\"/image.png\" onerror=\"alert(1)\">"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<MarkdownPreviewResponse>();
        Assert.NotNull(preview);
        Assert.Contains("<strong>Safe</strong>", preview.Html);
        Assert.DoesNotContain("onerror", preview.Html, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpClient> CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(AuthWebApplicationFactory.AdminLogin, AuthWebApplicationFactory.AdminPassword));
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }
}
