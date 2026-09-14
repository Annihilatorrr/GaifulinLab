namespace GaifulinLab.Infrastructure.Authentication;

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt, string RefreshToken);
