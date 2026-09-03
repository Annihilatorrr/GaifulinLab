using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GaifulinLab.Api.Tests.Authentication;

public sealed class AuthEndpointsTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Register_WithValidCredentials_CreatesStandardUser()
    {
        var login = $"new-user-{Guid.NewGuid():N}";
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(login, "Strong-password-1!"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var registration = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.Equal(login, registration?.Login);

        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(login);
        Assert.NotNull(user);
        Assert.False(await userManager.IsInRoleAsync(user, IdentityRoles.Admin));
    }

    [Fact]
    public async Task Register_WithDuplicateLogin_ReturnsConflict()
    {
        var login = $"duplicate-{Guid.NewGuid():N}";
        using var client = factory.CreateClient();
        var request = new RegisterRequest(login, "Strong-password-1!");

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("login_taken", error?.Code);
    }

    [Fact]
    public async Task Register_WithWeakPassword_ReturnsPasswordValidationErrors()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"weak-{Guid.NewGuid():N}", "password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_registration", error?.Code);
        Assert.True(error?.Errors?.ContainsKey("Password"));
    }

    [Fact]
    public async Task Register_AfterRateLimitIsExceeded_ReturnsTooManyRequests()
    {
        await using var isolatedFactory = new AuthWebApplicationFactory();
        using var client = isolatedFactory.CreateClient();

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            response?.Dispose();
            response = await client.PostAsJsonAsync(
                "/api/auth/register",
                new RegisterRequest("x", "x"));
        }

        using var finalResponse = Assert.IsType<HttpResponseMessage>(response);
        Assert.Equal(HttpStatusCode.TooManyRequests, finalResponse.StatusCode);
    }

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

    [Fact]
    public async Task Login_WithIdentityUserWithoutAdminRole_ReturnsTokenButAdminSessionIsForbidden()
    {
        const string userLogin = "second-user";
        const string userPassword = "Second-user-password-1!";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (await userManager.FindByNameAsync(userLogin) is null)
            {
                var result = await userManager.CreateAsync(
                    new ApplicationUser { UserName = userLogin },
                    userPassword);
                Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
            }
        }

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userLogin, userPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/auth/session")).StatusCode);
    }
}

public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminLogin = "admin";
    public const string AdminPassword = "correct-horse-battery-staple";

    private readonly string _mediaStoragePath = Path.Combine(
        Path.GetTempPath(),
        $"gaifulinlab-api-media-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var admin = new ApplicationUser { UserName = AdminLogin };
        var passwordHash = new PasswordHasher<ApplicationUser>().HashPassword(admin, AdminPassword);
        var databaseName = $"gaifulinlab-api-tests-{Guid.NewGuid()}";

        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.UseSetting(
            "ConnectionStrings:Postgres",
            "Host=localhost;Database=gaifulinlab_tests;Username=test;Password=test");
        builder.UseSetting("IDENTITY_BOOTSTRAP_ADMIN_LOGIN", AdminLogin);
        builder.UseSetting("IDENTITY_BOOTSTRAP_ADMIN_PASSWORD_HASH", passwordHash);
        builder.UseSetting("JWT_ISSUER", "GaifulinLab.Tests");
        builder.UseSetting("JWT_AUDIENCE", "GaifulinLab.Tests.Client");
        builder.UseSetting("JWT_SIGNING_KEY", "test-signing-key-that-is-at-least-32-bytes-long");
        builder.UseSetting("JWT_LIFETIME_MINUTES", "5");
        builder.UseSetting("MEDIA_STORAGE_PATH", _mediaStoragePath);
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5172");
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
