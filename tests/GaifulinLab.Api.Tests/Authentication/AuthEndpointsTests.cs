using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GaifulinLab.Api.Tests.Authentication;

public sealed class AuthEndpointsTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Register_WithValidCredentials_CreatesStandardUser()
    {
        var login = $"new-user-{Guid.NewGuid():N}@example.com";
        const string displayName = "Ada Lovelace";
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(login, displayName, "Strong-password-1!"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var registration = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.Equal(login, registration?.Login);

        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(login);
        Assert.NotNull(user);
        Assert.Equal(login, user.Email);
        Assert.Equal(displayName, user.DisplayName);
        Assert.False(await userManager.IsInRoleAsync(user, IdentityRoles.Admin));
    }

    [Fact]
    public async Task Register_WithDuplicateLogin_ReturnsConflict()
    {
        var login = $"duplicate-{Guid.NewGuid():N}@example.com";
        using var client = factory.CreateClient();
        var request = new RegisterRequest(login, "Ada Lovelace", "Strong-password-1!");

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("login_taken", error?.Code);
    }

    [Fact]
    public async Task Register_WithNonEmailLogin_ReturnsEmailValidationError()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("not-an-email", "Ada Lovelace", "Strong-password-1!"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_registration", error?.Code);
        Assert.Equal("Enter a valid email address.", error?.Errors?["Login"].Single());
    }

    [Fact]
    public async Task Register_WithWeakPassword_ReturnsPasswordValidationErrors()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"weak-{Guid.NewGuid():N}@example.com", "Ada Lovelace", "password"));

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
                new RegisterRequest("x", "X", "x"));
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
    public async Task Profile_CanBeReadAndUpdatedByTheAuthenticatedUser()
    {
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(AuthWebApplicationFactory.AdminLogin, AuthWebApplicationFactory.AdminPassword));
        var token = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var current = await client.GetFromJsonAsync<UserProfileResponse>("/api/auth/profile");
        Assert.Equal("Administrator", current?.DisplayName);

        var updated = await client.PutAsJsonAsync(
            "/api/auth/profile",
            new UpdateProfileRequest("Grace Hopper"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Grace Hopper", (await updated.Content.ReadFromJsonAsync<UserProfileResponse>())?.DisplayName);
        Assert.Equal("Grace Hopper", (await client.GetFromJsonAsync<UserProfileResponse>("/api/auth/profile"))?.DisplayName);
    }

    [Fact]
    public async Task Register_WithInvalidDisplayName_ReturnsValidationError()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"invalid-name-{Guid.NewGuid():N}@example.com", " ", "Strong-password-1!"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_registration", error?.Code);
        Assert.Equal("Display name must be between 2 and 100 characters.", error?.Errors?["DisplayName"].Single());
    }

    [Fact]
    public async Task Login_WithIdentityUserWithoutAdminRole_ReturnsTokenAndWorkspaceSession()
    {
        const string userLogin = "second-user";
        const string userPassword = "Second-user-password-1!";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (await userManager.FindByNameAsync(userLogin) is null)
            {
                var result = await userManager.CreateAsync(
                    new ApplicationUser { UserName = userLogin, DisplayName = "Second User" },
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

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/auth/session")).StatusCode);
    }
}

public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminLogin = "admin";
    public const string AdminPassword = "Correct-horse-battery-staple-1!";

    private readonly string _mediaStoragePath = Path.Combine(
        Path.GetTempPath(),
        $"gaifulinlab-api-media-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var databaseName = $"gaifulinlab-api-tests-{Guid.NewGuid()}";

        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.UseSetting(
            "ConnectionStrings:Postgres",
            "Host=localhost;Database=gaifulinlab_tests;Username=test;Password=test");
        builder.UseSetting("JWT_ISSUER", "GaifulinLab.Tests");
        builder.UseSetting("JWT_AUDIENCE", "GaifulinLab.Tests.Client");
        builder.UseSetting("JWT_SIGNING_KEY", "test-signing-key-that-is-at-least-32-bytes-long");
        builder.UseSetting("JWT_LIFETIME_MINUTES", "5");
        builder.UseSetting("Analytics:ViewHashKey", "test-view-hash-key-that-is-at-least-32-bytes-long");
        builder.UseSetting("ForwardedHeaders:TrustedNetworks:0", "127.0.0.1/32");
        builder.UseSetting("MEDIA_STORAGE_PATH", _mediaStoragePath);
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5172");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!roleManager.RoleExistsAsync(IdentityRoles.Admin).GetAwaiter().GetResult())
        {
            var roleResult = roleManager.CreateAsync(new IdentityRole(IdentityRoles.Admin)).GetAwaiter().GetResult();
            Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(error => error.Description)));
        }

        var user = userManager.FindByNameAsync(AdminLogin).GetAwaiter().GetResult();
        if (user is null)
        {
            user = new ApplicationUser { UserName = AdminLogin, DisplayName = "Administrator" };
            var userResult = userManager.CreateAsync(user, AdminPassword).GetAwaiter().GetResult();
            Assert.True(userResult.Succeeded, string.Join("; ", userResult.Errors.Select(error => error.Description)));
        }

        if (!userManager.IsInRoleAsync(user, IdentityRoles.Admin).GetAwaiter().GetResult())
        {
            var roleResult = userManager.AddToRoleAsync(user, IdentityRoles.Admin).GetAwaiter().GetResult();
            Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(error => error.Description)));
        }

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (Directory.Exists(_mediaStoragePath))
        {
            Directory.Delete(_mediaStoragePath, recursive: true);
        }
    }

    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextRequest) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                await nextRequest();
            });
            next(app);
        };
    }
}
