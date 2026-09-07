using System.Net;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Common;

namespace GaifulinLab.Web.Authentication;

public sealed class RegistrationClient(HttpClient httpClient)
{
    public async Task<RegistrationAttemptResult> RegisterAsync(
        string login,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "/api/auth/register",
                new RegisterRequest(login, displayName, password),
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var result = await response.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken);
                return result is null || string.IsNullOrWhiteSpace(result.Login)
                    ? RegistrationAttemptResult.Failure("The server returned an invalid response.")
                    : RegistrationAttemptResult.Success(result.Login);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return RegistrationAttemptResult.Failure(
                    "Too many registration attempts. Please try again later.");
            }

            var error = await TryReadErrorAsync(response, cancellationToken);
            return RegistrationAttemptResult.Failure(
                error?.Errors?.Values.SelectMany(messages => messages).FirstOrDefault()
                    ?? error?.Message
                    ?? "Registration is temporarily unavailable.");
        }
        catch (HttpRequestException)
        {
            return RegistrationAttemptResult.Failure("Unable to reach the server.");
        }
    }

    private static async Task<ApiErrorResponse?> TryReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

public sealed record RegistrationAttemptResult(
    bool Succeeded,
    string? Login,
    string? ErrorMessage)
{
    public static RegistrationAttemptResult Success(string login) => new(true, login, null);

    public static RegistrationAttemptResult Failure(string message) => new(false, null, message);
}
