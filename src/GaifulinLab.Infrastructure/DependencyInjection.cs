using GaifulinLab.Application.Content;
using GaifulinLab.Application.Media;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Content;
using GaifulinLab.Infrastructure.Media;
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

        services.AddAdminAuthentication(configuration);

        return services;
    }
}
