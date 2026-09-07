using System.Net;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Web.Authentication;

namespace GaifulinLab.Web.Tests.Authentication;

public sealed class RegistrationClientTests
{
    [Fact]
    public async Task RegisterAsync_PostsCredentialsAndReturnsCreatedLogin()
    {
        RegisterRequest? request = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async message =>
        {
            request = await message.Content!.ReadFromJsonAsync<RegisterRequest>();
            return JsonResponse(new RegisterResponse("new-user@example.com"), HttpStatusCode.Created);
        }))
        {
            BaseAddress = new Uri("http://localhost:5180/")
        };
        var client = new RegistrationClient(httpClient);

        var result = await client.RegisterAsync("new-user@example.com", "Ada Lovelace", "Strong-password-1!");

        Assert.True(result.Succeeded);
        Assert.Equal("new-user@example.com", result.Login);
        Assert.Equal("new-user@example.com", request?.Login);
        Assert.Equal("Ada Lovelace", request?.DisplayName);
        Assert.Equal("Strong-password-1!", request?.Password);
    }

    [Fact]
    public async Task RegisterAsync_ReturnsFirstServerValidationMessage()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => Task.FromResult(
            JsonResponse(
                new ApiErrorResponse(
                    "invalid_registration",
                    "Registration failed.",
                    new Dictionary<string, string[]> { ["Password"] = ["Password needs a symbol."] }),
                HttpStatusCode.BadRequest))))
        {
            BaseAddress = new Uri("http://localhost:5180/")
        };
        var client = new RegistrationClient(httpClient);

        var result = await client.RegisterAsync("new-user@example.com", "Ada Lovelace", "password");

        Assert.False(result.Succeeded);
        Assert.Equal("Password needs a symbol.", result.ErrorMessage);
    }

    [Fact]
    public async Task RegisterAsync_WhenRateLimited_ReturnsHelpfulMessage()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests))))
        {
            BaseAddress = new Uri("http://localhost:5180/")
        };
        var client = new RegistrationClient(httpClient);

        var result = await client.RegisterAsync("new-user@example.com", "Ada Lovelace", "Strong-password-1!");

        Assert.False(result.Succeeded);
        Assert.Contains("Too many", result.ErrorMessage);
    }

    private static HttpResponseMessage JsonResponse<T>(T value, HttpStatusCode statusCode) =>
        new(statusCode)
        {
            Content = JsonContent.Create(value)
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request);
    }
}
