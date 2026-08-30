using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GaifulinLab.Infrastructure.Authentication;

internal static class AuthenticationConfiguration
{
    private const int DefaultTokenLifetimeMinutes = 30;
    private const int MinimumSigningKeyBytes = 32;

    public static IServiceCollection AddAdminAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = ReadSettings(configuration);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.JwtSigningKey));

        services.AddSingleton(settings);
        services.AddSingleton<AdminPasswordHasher>();
        services.AddSingleton<IAdminAuthenticationService, AdminAuthenticationService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = settings.JwtAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.UniqueName
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Admin, policy =>
                policy.RequireAuthenticatedUser());

        return services;
    }

    private static AdminAuthenticationSettings ReadSettings(IConfiguration configuration)
    {
        var login = ReadRequired(configuration, "ADMIN_LOGIN", "Admin:Login");
        var passwordHash = ReadRequired(configuration, "ADMIN_PASSWORD_HASH", "Admin:PasswordHash");
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

        return new AdminAuthenticationSettings(
            login,
            passwordHash,
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
