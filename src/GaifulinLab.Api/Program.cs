using System.Threading.RateLimiting;
using GaifulinLab.Api.Configuration;
using GaifulinLab.Api.Filters;
using GaifulinLab.Application;
using GaifulinLab.Infrastructure;
using GaifulinLab.Infrastructure.Analytics;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPermitLimit", 5);
var registrationPermitLimit = builder.Configuration.GetValue("RateLimiting:RegistrationPermitLimit", 5);

builder.Services.AddControllers(options => options.Filters.Add<ApiExceptionFilter>());
builder.Services.AddProblemDetails();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(new ArticleViewVisitorHasher(builder.Configuration));
builder.Services.AddIdentityAuthentication(builder.Configuration);
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy(
        ApiCorsPolicies.Frontend,
        policy => policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()));
}
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ApiRateLimitPolicies.Login, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy(ApiRateLimitPolicies.Registration, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = registrationPermitLimit,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy(ApiRateLimitPolicies.ArticlePdf, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

await using (var searchScope = app.Services.CreateAsyncScope())
{
    var database = searchScope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (database.Database.IsRelational()) await database.BackfillArticleSearchAsync();
}

if (app.Environment.IsDevelopment())
{
    await app.Services.SeedTaxonomyAsync(app.Configuration);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseForwardedHeaders(ApiForwardedHeadersConfiguration.Create(app.Configuration));

if (app.Configuration.GetValue("HTTPS_REDIRECT_ENABLED", true))
{
    app.UseHttpsRedirection();
}

if (corsOrigins.Length > 0)
{
    app.UseCors(ApiCorsPolicies.Frontend);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program;
