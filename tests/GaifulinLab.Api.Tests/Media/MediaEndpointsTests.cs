using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Media;
using GaifulinLab.Infrastructure.Persistence;
using GaifulinLab.Api.Tests.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GaifulinLab.Api.Tests.Media;

public sealed class MediaEndpointsTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task Upload_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();
        using var content = CreateUploadContent(OnePixelPng, "pixel.png", "image/png");

        var response = await client.PostAsync("/api/admin/media", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ValidImage_PersistsAndServesImmutableFile()
    {
        using var client = await CreateAuthenticatedClient();
        using var content = CreateUploadContent(OnePixelPng, "pixel.png", "image/png");

        var uploadResponse = await client.PostAsync("/api/admin/media", content);

        Assert.True(
            uploadResponse.StatusCode == HttpStatusCode.Created,
            $"Expected 201, received {(int)uploadResponse.StatusCode}: {await uploadResponse.Content.ReadAsStringAsync()}");
        var upload = await uploadResponse.Content.ReadFromJsonAsync<UploadMediaResponse>();
        Assert.NotNull(upload);
        Assert.Equal($"/media/{upload.Id}", upload.Url);
        Assert.Equal("pixel.png", upload.OriginalFileName);
        Assert.Equal("image/png", upload.ContentType);
        Assert.Equal(OnePixelPng.Length, upload.Size);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var asset = await dbContext.MediaAssets.SingleAsync(candidate => candidate.Id == upload.Id);
            Assert.EndsWith(".png", asset.StoredFileName, StringComparison.Ordinal);
            Assert.DoesNotContain("pixel", asset.StoredFileName, StringComparison.OrdinalIgnoreCase);
        }

        using var publicClient = factory.CreateClient();
        var fileResponse = await publicClient.GetAsync(upload.Url);
        Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
        Assert.Equal("image/png", fileResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", fileResponse.Headers.CacheControl?.ToString());
        Assert.Equal(OnePixelPng, await fileResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_WhenSignatureDoesNotMatchDeclaration_ReturnsBadRequest()
    {
        using var client = await CreateAuthenticatedClient();
        using var content = CreateUploadContent(OnePixelPng, "not-really-jpeg.jpg", "image/jpeg");

        var response = await client.PostAsync("/api/admin/media", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_request", error?.Code);
    }

    private static MultipartFormDataContent CreateUploadContent(
        byte[] bytes,
        string fileName,
        string contentType)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        multipart.Add(fileContent, "file", fileName);
        return multipart;
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
