namespace GaifulinLab.Infrastructure.Authentication;

public interface IUserAuthenticationService
{
    Task<IssuedAccessToken?> AuthenticateAsync(string login, string password);

    Task<UserRegistrationResult> RegisterAsync(string login, string displayName, string password);

    Task<string?> GetDisplayNameAsync(string userId);

    Task<bool> UpdateDisplayNameAsync(string userId, string displayName);
}
