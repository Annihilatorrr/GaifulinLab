using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Articles;
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
    private static readonly SemaphoreSlim AdminAuthenticationLock = new(1, 1);
    private static string? _adminAccessToken;
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    public static TheoryData<byte[], string, string> SupportedImages => new()
    {
        { OnePixelPng, "diagram.png", "image/png" },
        { [0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10], "photo.jpeg", "image/jpeg" },
        { "GIF89a-payload"u8.ToArray(), "animation.gif", "image/gif" },
        { "RIFF0000WEBPpayload"u8.ToArray(), "drawing.webp", "image/webp" }
    };

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

    [Theory]
    [MemberData(nameof(SupportedImages))]
    public async Task Upload_SupportedImageFormats_AreStoredAndPubliclyDownloadable(
        byte[] bytes,
        string fileName,
        string contentType)
    {
        using var client = await CreateAuthenticatedClient();
        using var content = CreateUploadContent(bytes, fileName, contentType);

        var response = await client.PostAsync("/api/admin/media", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var upload = await response.Content.ReadFromJsonAsync<UploadMediaResponse>();
        Assert.NotNull(upload);
        Assert.Equal(fileName, upload.OriginalFileName);
        Assert.Equal(contentType, upload.ContentType);
        Assert.Equal(bytes, await client.GetByteArrayAsync(upload.Url));
    }

    [Fact]
    public async Task Upload_UnsupportedOrInvalidImage_ReturnsClearValidationError()
    {
        using var client = await CreateAuthenticatedClient();
        using var unsupported = CreateUploadContent("plain text"u8.ToArray(), "notes.txt", "text/plain");
        using var mismatched = CreateUploadContent(OnePixelPng, "photo.jpg", "image/jpeg");

        var unsupportedResponse = await client.PostAsync("/api/admin/media", unsupported);
        var mismatchResponse = await client.PostAsync("/api/admin/media", mismatched);

        Assert.Equal(HttpStatusCode.BadRequest, unsupportedResponse.StatusCode);
        Assert.Contains(
            "Only PNG, JPEG, GIF and WebP images are supported.",
            (await unsupportedResponse.Content.ReadFromJsonAsync<ApiErrorResponse>())?.Message);
        Assert.Equal(HttpStatusCode.BadRequest, mismatchResponse.StatusCode);
        Assert.Contains(
            "do not match",
            (await mismatchResponse.Content.ReadFromJsonAsync<ApiErrorResponse>())?.Message);
    }

    [Fact]
    public async Task Upload_RegularAuthor_CanPublishArticleWithReaderAccessibleImage()
    {
        var login = $"media-author-{Guid.NewGuid():N}@example.com";
        const string password = "Strong-password-1!";
        using var client = factory.CreateClient();
        var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(login, "Media Author", password));
        registration.EnsureSuccessStatusCode();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(login, password));
        loginResponse.EnsureSuccessStatusCode();
        var session = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);
        using var content = CreateUploadContent(OnePixelPng, "author-image.png", "image/png");

        var response = await client.PostAsync("/api/admin/media", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var upload = await response.Content.ReadFromJsonAsync<UploadMediaResponse>();
        var slug = $"author-image-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest(
                "en",
                "Author image article",
                "Image uploaded by a regular author",
                $"![author image]({upload!.Url})",
                slug));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();
        var publishResponse = await client.PostAsync(
            $"/api/admin/articles/{created!.ArticleId}/localizations/en/publish",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, publishResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var publicArticle = await client.GetFromJsonAsync<PublicArticleDetailsDto>(
            $"/api/public/articles/en/{slug}");
        Assert.Contains($"src=\"{upload.Url}\"", publicArticle!.Html);
        var imageResponse = await client.GetAsync(upload.Url);
        Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
        Assert.Equal(OnePixelPng, await imageResponse.Content.ReadAsByteArrayAsync());
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
        await AdminAuthenticationLock.WaitAsync();
        try
        {
            if (_adminAccessToken is null)
            {
                var response = await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new LoginRequest(AuthWebApplicationFactory.AdminLogin, AuthWebApplicationFactory.AdminPassword));
                response.EnsureSuccessStatusCode();
                _adminAccessToken = (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
            }
        }
        finally
        {
            AdminAuthenticationLock.Release();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _adminAccessToken);
        return client;
    }
}
