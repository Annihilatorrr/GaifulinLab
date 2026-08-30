using System.Net;
using GaifulinLab.Api.Tests.Authentication;

namespace GaifulinLab.Api.Tests.Cors;

public sealed class CorsTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Preflight_FromConfiguredFrontendOrigin_IsAllowed()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/public/articles?languageCode=en");
        request.Headers.Add("Origin", "http://localhost:5172");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            "http://localhost:5172",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("GET", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
    }
}
