using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaifulinLab.Api.Tests.Authentication;
using GaifulinLab.Contracts.Common;
using Microsoft.AspNetCore.Mvc;

namespace GaifulinLab.Api.Tests.PublicContent;

public sealed class PublicSearchValidationTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Search_ValidatesAnOverlongQueryWithAUsefulError()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/public/search?q=" + new string('a', 201));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("invalid_request", error.Code);
        Assert.Equal("Invalid search parameters.", error.Message);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483648")]
    [InlineData("scope=unknown")]
    [InlineData("sort=unknown")]
    [InlineData("period=unknown")]
    [InlineData("languageCode=invalid")]
    public async Task Search_RejectsInvalidParametersWithFieldErrors(string query)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/public/search?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(error);
        Assert.NotEmpty(error.Errors);
    }

    [Theory]
    [InlineData("topic=" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("tag=")]
    [InlineData("tag=" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("tag=one&tag=two&tag=three&tag=four&tag=five&tag=six&tag=seven&tag=eight&tag=nine&tag=ten&tag=eleven&tag=twelve&tag=thirteen&tag=fourteen&tag=fifteen&tag=sixteen&tag=seventeen&tag=eighteen&tag=nineteen&tag=twenty&tag=twenty-one")]
    public async Task Search_RejectsInvalidTopicAndTagFilters(string query)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/public/search?" + query);

        await AssertClearBadRequest(response);
    }

    private static async Task AssertClearBadRequest(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payload = document.RootElement;

        if (payload.TryGetProperty("code", out var code))
        {
            Assert.Equal("invalid_request", code.GetString());
            Assert.Equal("Invalid search parameters.", payload.GetProperty("message").GetString());
            return;
        }

        Assert.True(payload.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.NotEmpty(errors.EnumerateObject());
    }
}
