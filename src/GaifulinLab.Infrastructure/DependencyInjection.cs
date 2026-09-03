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

        var pdfTimeoutSeconds = Math.Clamp(
            configuration.GetValue("PDF_RENDERER_TIMEOUT_SECONDS", 45),
            5,
            120);
        var maximumConcurrentPdfRenders = Math.Clamp(
            configuration.GetValue("PDF_MAX_CONCURRENT_RENDERS", 2),
            1,
            8);

        var mathJaxAssetsPath = configuration["PDF_MATHJAX_ASSETS_PATH"]
            ?? Path.Combine(AppContext.BaseDirectory, "pdf-assets");
        services.AddSingleton(new ArticlePdfRendererSettings(
            TimeSpan.FromSeconds(pdfTimeoutSeconds),
            maximumConcurrentPdfRenders,
            configuration["PDF_MATHJAX_PATH"]
            ?? Path.Combine(mathJaxAssetsPath, "mathjax", "tex-chtml.js"),
            mathJaxAssetsPath));
        services.AddSingleton<IArticlePdfRenderer, PlaywrightArticlePdfRenderer>();

        return services;
    }

    public static IServiceCollection AddPdfExportWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var pdfTimeoutSeconds = Math.Clamp(
            configuration.GetValue("PDF_RENDERER_TIMEOUT_SECONDS", 45),
            5,
            120);
        var pollIntervalMilliseconds = Math.Clamp(
            configuration.GetValue("PDF_WORKER_POLL_INTERVAL_MILLISECONDS", 1_000),
            100,
            10_000);
        var leaseSeconds = Math.Clamp(
            configuration.GetValue("PDF_WORKER_LEASE_SECONDS", 120),
            pdfTimeoutSeconds + 15,
            600);

        services.AddSingleton(new PdfExportWorkerSettings(
            TimeSpan.FromMilliseconds(pollIntervalMilliseconds),
            TimeSpan.FromSeconds(leaseSeconds)));
        services.AddHostedService<PdfExportWorker>();

        return services;
    }

    public static IServiceCollection AddIdentityAuthentication(
        this IServiceCollection services,
        IConfiguration configuration) =>
        AuthenticationConfiguration.AddIdentityAuthentication(services, configuration);
}
