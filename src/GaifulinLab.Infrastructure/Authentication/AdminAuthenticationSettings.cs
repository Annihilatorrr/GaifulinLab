namespace GaifulinLab.Infrastructure.Authentication;

internal sealed record AdminAuthenticationSettings(
    string Login,
    string PasswordHash,
    string JwtIssuer,
    string JwtAudience,
    string JwtSigningKey,
    TimeSpan TokenLifetime);
