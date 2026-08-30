using System.Net;
using System.Net.Http.Json;
using GaifulinLab.Web.Tests.Authentication;

namespace GaifulinLab.Web.Tests.Health;

public sealed class HealthEndpointsTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoint_is_anonymous_and_reports_healthy(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("Healthy", body?.Status);
    }

    private sealed record HealthResponse(string Status);
}
