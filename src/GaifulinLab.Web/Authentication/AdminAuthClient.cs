using System.Net;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Auth;

namespace GaifulinLab.Web.Authentication;

public sealed class AdminAuthClient(
    HttpClient httpClient,
    TokenAuthenticationStateProvider authenticationStateProvider)
{
    public async Task<LoginAttemptResult> LoginAsync(
        string login,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(login, password),
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return LoginAttemptResult.Failure("Invalid login or password.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return LoginAttemptResult.Failure("Sign in is temporarily unavailable.");
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
            if (result is null || string.IsNullOrWhiteSpace(result.AccessToken))
            {
                return LoginAttemptResult.Failure("The server returned an invalid response.");
            }

            await authenticationStateProvider.SetTokenAsync(result.AccessToken);
            return LoginAttemptResult.Success;
        }
        catch (HttpRequestException)
        {
            return LoginAttemptResult.Failure("Unable to reach the server.");
        }
    }

    public Task LogoutAsync() => authenticationStateProvider.ClearTokenAsync();
}

public sealed record LoginAttemptResult(bool Succeeded, string? ErrorMessage)
{
    public static LoginAttemptResult Success { get; } = new(true, null);

    public static LoginAttemptResult Failure(string message) => new(false, message);
}
