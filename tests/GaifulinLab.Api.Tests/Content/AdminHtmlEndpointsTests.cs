using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Content;
using GaifulinLab.Api.Tests.Authentication;

namespace GaifulinLab.Api.Tests.Content;

public sealed class AdminHtmlEndpointsTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Preview_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/admin/html/preview",
            new HtmlPreviewRequest("<p>Preview</p>"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Preview_WithBearerToken_ReturnsSanitizedHtml()
    {
        using var client = await CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/api/admin/html/preview",
            new HtmlPreviewRequest("<p>Safe</p><img src=\"/image.png\" onerror=\"alert(1)\"><script>alert(1)</script>"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<HtmlPreviewResponse>();
        Assert.NotNull(preview);
        Assert.Contains("<p>Safe</p>", preview.Html);
        Assert.DoesNotContain("onerror", preview.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", preview.Html, StringComparison.OrdinalIgnoreCase);
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
