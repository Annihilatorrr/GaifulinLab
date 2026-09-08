using System.Net;
using GaifulinLab.Api.Tests.Authentication;

namespace GaifulinLab.Api.Tests.PublicContent;

public sealed class PublicSearchValidationTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Search_ValidatesTheShortQueryParameter()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/public/search?q=" + new string('a', 201));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483648")]
    [InlineData("scope=unknown")]
    [InlineData("sort=unknown")]
    [InlineData("period=unknown")]
    [InlineData("languageCode=invalid")]
    public async Task Search_RejectsInvalidParametersBeforeExecutingSql(string query)
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/public/search?" + query)).StatusCode);
    }
}
