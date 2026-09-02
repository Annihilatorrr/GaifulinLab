namespace GaifulinLab.Infrastructure.Authentication;

internal sealed record JwtAuthenticationSettings(
    string Issuer,
    string Audience,
    string SigningKey,
    TimeSpan TokenLifetime);
