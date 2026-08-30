using System.Threading.RateLimiting;
using GaifulinLab.Api.Configuration;
using GaifulinLab.Application;
using GaifulinLab.Infrastructure;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;

if (args is ["--hash-admin-password"])
{
    var password = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
    ArgumentException.ThrowIfNullOrWhiteSpace(password);
    Console.WriteLine(new AdminPasswordHasher().Hash(password));
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ApiRateLimitPolicies.Login, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("APPLY_DATABASE_MIGRATIONS"))
{
    await app.Services.ApplyDatabaseMigrationsAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

if (app.Configuration.GetValue("HTTPS_REDIRECT_ENABLED", true))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program;
