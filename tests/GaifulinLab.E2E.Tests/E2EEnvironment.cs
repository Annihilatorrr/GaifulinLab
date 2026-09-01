namespace GaifulinLab.E2E.Tests;

public sealed class E2EEnvironment : IAsyncLifetime
{
    private const string DefaultBaseUrl = "http://localhost:5172";

    public Uri BaseUri { get; private set; } = null!;
    private string AdminLogin { get; set; } = null!;
    private string AdminPassword { get; set; } = null!;

    public Task InitializeAsync()
    {
        var configuredBaseUrl = Environment.GetEnvironmentVariable("GAIFULINLAB_E2E_BASE_URL")
            ?? DefaultBaseUrl;
        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp
                && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "GAIFULINLAB_E2E_BASE_URL must be an absolute HTTP or HTTPS URL.");
        }

        AdminLogin = Environment.GetEnvironmentVariable("GAIFULINLAB_E2E_ADMIN_LOGIN") ?? "admin";
        AdminPassword = Environment.GetEnvironmentVariable("GAIFULINLAB_E2E_ADMIN_PASSWORD")
            ?? throw new InvalidOperationException(
                "Set GAIFULINLAB_E2E_ADMIN_PASSWORD before running E2E tests. "
                + "It is intentionally not stored in the repository.");
        if (string.IsNullOrWhiteSpace(AdminPassword))
        {
            throw new InvalidOperationException("GAIFULINLAB_E2E_ADMIN_PASSWORD cannot be empty.");
        }
        BaseUri = baseUri;
        return Task.CompletedTask;
    }

    public (string Login, string Password) GetAdminCredentials() => (AdminLogin, AdminPassword);

    public Task DisposeAsync() => Task.CompletedTask;
}
