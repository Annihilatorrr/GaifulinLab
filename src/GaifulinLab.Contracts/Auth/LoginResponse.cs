namespace GaifulinLab.Contracts.Auth;

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt);
