using GaifulinLab.Application;
using GaifulinLab.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;

var bootstrapDevelopment = IsDevelopmentEnvironment();
Log.Logger = ConfigureConsole(
        new LoggerConfiguration()
            .MinimumLevel.Information(),
        bootstrapDevelopment)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting GaifulinLab PDF worker");

    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Services.AddSerilog((services, loggerConfiguration) =>
    {
        loggerConfiguration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();
        ConfigureConsole(loggerConfiguration, bootstrapDevelopment);
    });

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddPdfExportWorker(builder.Configuration);

    await builder.Build().RunAsync();
}
catch (Exception exception)
{
    Log.Fatal(exception, "GaifulinLab PDF worker terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static LoggerConfiguration ConfigureConsole(
    LoggerConfiguration loggerConfiguration,
    bool isDevelopment) =>
    isDevelopment
        ? loggerConfiguration.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
        : loggerConfiguration.WriteTo.Console(new RenderedCompactJsonFormatter());

static bool IsDevelopmentEnvironment()
{
    var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    return string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);
}
