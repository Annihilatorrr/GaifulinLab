namespace GaifulinLab.Infrastructure.Authentication;

public interface IAdminAuthenticationService
{
    IssuedAccessToken? Authenticate(string login, string password);
}
