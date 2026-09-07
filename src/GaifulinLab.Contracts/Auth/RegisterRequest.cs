namespace GaifulinLab.Contracts.Auth;

public sealed record RegisterRequest(string Login, string DisplayName, string Password);
