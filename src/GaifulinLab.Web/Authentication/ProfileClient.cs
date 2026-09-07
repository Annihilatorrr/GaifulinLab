using System.Net.Http.Json;
using GaifulinLab.Contracts.Auth;

namespace GaifulinLab.Web.Authentication;

public sealed class ProfileClient(HttpClient httpClient)
{
    public async Task<UserProfileResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/api/auth/profile", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserProfileResponse>(cancellationToken)
            ?? throw new HttpRequestException("The server returned an invalid profile response.");
    }

    public async Task<UserProfileResponse> UpdateAsync(
        string displayName,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            "/api/auth/profile",
            new UpdateProfileRequest(displayName),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserProfileResponse>(cancellationToken)
            ?? throw new HttpRequestException("The server returned an invalid profile response.");
    }
}
