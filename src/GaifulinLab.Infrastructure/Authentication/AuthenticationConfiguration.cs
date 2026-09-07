using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GaifulinLab.Application.Authors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GaifulinLab.Infrastructure.Authentication;

internal static class AuthenticationConfiguration
{
    private const int DefaultTokenLifetimeMinutes = 30;
    private const int MinimumSigningKeyBytes = 32;

    public static IServiceCollection AddIdentityAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = ReadSettings(configuration);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));

        services.AddSingleton(settings);
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 8;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<Persistence.AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<IAuthorDisplayNameLookup, IdentityAuthorDisplayNameLookup>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = ClaimTypes.Role
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Admin, policy =>
                policy.RequireRole(IdentityRoles.Admin));

        return services;
    }

    private static JwtAuthenticationSettings ReadSettings(IConfiguration configuration)
    {
        var issuer = ReadRequired(configuration, "JWT_ISSUER", "Jwt:Issuer");
        var audience = ReadRequired(configuration, "JWT_AUDIENCE", "Jwt:Audience");
        var signingKey = ReadRequired(configuration, "JWT_SIGNING_KEY", "Jwt:SigningKey");

        if (Encoding.UTF8.GetByteCount(signingKey) < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"JWT signing key must contain at least {MinimumSigningKeyBytes} UTF-8 bytes.");
        }

        var lifetimeValue = configuration["JWT_LIFETIME_MINUTES"]
            ?? configuration["Jwt:LifetimeMinutes"];
        var lifetimeMinutes = DefaultTokenLifetimeMinutes;

        if (lifetimeValue is not null
            && (!int.TryParse(lifetimeValue, NumberStyles.None, CultureInfo.InvariantCulture, out lifetimeMinutes)
                || lifetimeMinutes is < 1 or > 1_440))
        {
            throw new InvalidOperationException("JWT lifetime must be between 1 and 1440 minutes.");
        }

        return new JwtAuthenticationSettings(
            issuer,
            audience,
            signingKey,
            TimeSpan.FromMinutes(lifetimeMinutes));
    }

    private static string ReadRequired(
        IConfiguration configuration,
        string environmentKey,
        string sectionKey)
    {
        var value = configuration[environmentKey] ?? configuration[sectionKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Authentication setting '{environmentKey}' is not configured.");
        }

        return value;
    }
}
