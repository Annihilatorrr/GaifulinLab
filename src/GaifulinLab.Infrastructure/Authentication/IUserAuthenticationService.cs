namespace GaifulinLab.Infrastructure.Authentication;

public interface IUserAuthenticationService
{
    Task<IssuedAccessToken?> AuthenticateAsync(string login, string password);
}
