namespace GaifulinLab.Infrastructure.Authentication;

public interface IUserAuthenticationService
{
    Task<IssuedAccessToken?> AuthenticateAsync(string login, string password);

    Task<IssuedAccessToken?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);

    Task<UserRegistrationResult> RegisterAsync(string login, string displayName, string password);

    Task<string?> GetDisplayNameAsync(string userId);

    Task<UpdateDisplayNameResult> UpdateDisplayNameAsync(string userId, string displayName);
}

public enum UpdateDisplayNameResult
{
    Updated,
    NotFound,
    Conflict
}
