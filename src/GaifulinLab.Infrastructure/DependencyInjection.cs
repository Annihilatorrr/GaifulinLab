using GaifulinLab.Application.Content;
using GaifulinLab.Application.Media;
using GaifulinLab.Application.Pdf;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Content;
using GaifulinLab.Infrastructure.Media;
using GaifulinLab.Infrastructure.Pdf;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GaifulinLab.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsqlOptions.EnableRetryOnFailure();
            }));

        services.AddScoped<IAppDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<AppDbContext>());
        services.AddSingleton<IMarkdownRenderer, MarkdownRenderer>();
        services.AddSingleton<IMediaStorage>(_ => new FileSystemMediaStorage(
            configuration["MEDIA_STORAGE_PATH"]
            ?? configuration["MediaStorage:RootPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "media")));

        var pdfRendererBaseUri = GetAbsoluteHttpUri(
            configuration["PDF_RENDERER_BASE_URL"] ?? "http://localhost:3000",
            "PDF_RENDERER_BASE_URL");
        var pdfArticleBaseUri = GetAbsoluteHttpUri(
            configuration["PDF_ARTICLE_BASE_URL"] ?? "https://localhost:7069",
            "PDF_ARTICLE_BASE_URL");
        var pdfTimeoutSeconds = Math.Clamp(
            configuration.GetValue("PDF_RENDERER_TIMEOUT_SECONDS", 45),
            5,
            120);
        var maximumConcurrentPdfRenders = Math.Clamp(
            configuration.GetValue("PDF_MAX_CONCURRENT_RENDERS", 2),
            1,
            8);

        services.AddSingleton(new ArticlePdfRendererSettings(
            EnsureTrailingSlash(pdfArticleBaseUri),
            maximumConcurrentPdfRenders));
        services.AddHttpClient(GotenbergArticlePdfRenderer.HttpClientName, client =>
        {
            client.BaseAddress = EnsureTrailingSlash(pdfRendererBaseUri);
            client.Timeout = TimeSpan.FromSeconds(pdfTimeoutSeconds);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/pdf"));
        });
        services.AddSingleton<IArticlePdfRenderer, GotenbergArticlePdfRenderer>();

        services.AddAdminAuthentication(configuration);

        return services;
    }

    private static Uri GetAbsoluteHttpUri(string value, string settingName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{settingName} must be an absolute HTTP(S) URL.");
        }

        return uri;
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/");
}
