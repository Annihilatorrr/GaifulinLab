using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GaifulinLab.Web.Tests.Authentication;

public sealed class AuthEndpointsTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsUsableBearerToken()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(AuthWebApplicationFactory.AdminLogin, AuthWebApplicationFactory.AdminPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        Assert.NotEmpty(login.AccessToken);
        Assert.True(login.ExpiresAt > DateTimeOffset.UtcNow);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var sessionResponse = await client.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.NoContent, sessionResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsGenericUnauthorizedError()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(AuthWebApplicationFactory.AdminLogin, "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_credentials", error?.Code);
    }

    [Fact]
    public async Task Session_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminLogin = "admin";
    public const string AdminPassword = "correct-horse-battery-staple";

    private readonly string _mediaStoragePath = Path.Combine(
        Path.GetTempPath(),
        $"gaifulinlab-web-media-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var passwordHash = new AdminPasswordHasher().Hash(AdminPassword);
        var databaseName = $"gaifulinlab-web-tests-{Guid.NewGuid()}";

        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.UseSetting(
            "ConnectionStrings:Postgres",
            "Host=localhost;Database=gaifulinlab_tests;Username=test;Password=test");
        builder.UseSetting("ADMIN_LOGIN", AdminLogin);
        builder.UseSetting("ADMIN_PASSWORD_HASH", passwordHash);
        builder.UseSetting("JWT_ISSUER", "GaifulinLab.Tests");
        builder.UseSetting("JWT_AUDIENCE", "GaifulinLab.Tests.Client");
        builder.UseSetting("JWT_SIGNING_KEY", "test-signing-key-that-is-at-least-32-bytes-long");
        builder.UseSetting("JWT_LIFETIME_MINUTES", "5");
        builder.UseSetting("MEDIA_STORAGE_PATH", _mediaStoragePath);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (Directory.Exists(_mediaStoragePath))
        {
            Directory.Delete(_mediaStoragePath, recursive: true);
        }
    }
}
