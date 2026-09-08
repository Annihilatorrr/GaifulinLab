using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace GaifulinLab.Infrastructure.Authentication;

internal sealed class UserAuthenticationService(
    JwtAuthenticationSettings settings,
    UserManager<ApplicationUser> userManager,
    IServiceScopeFactory scopeFactory,
    ILogger<UserAuthenticationService> logger) : IUserAuthenticationService
{
    private const int MaximumIdentityConcurrencyRetries = 3;
    private const string ConcurrencyFailureCode = "ConcurrencyFailure";

    public async Task<IssuedAccessToken?> AuthenticateAsync(string login, string password)
    {
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(password);

        var user = await userManager.FindByNameAsync(login);
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await RecordFailedAccessAsync(user);
            return null;
        }

        if (!await ResetFailedAccessCountAsync(user))
        {
            return null;
        }

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.Add(settings.TokenLifetime);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange((await userManager.GetRolesAsync(user))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public async Task<UserRegistrationResult> RegisterAsync(string login, string displayName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(password);

        var result = await userManager.CreateAsync(
            new ApplicationUser
            {
                UserName = login,
                Email = login,
                DisplayName = displayName,
                SecurityStamp = Guid.NewGuid().ToString("N")
            },
            password);

        if (result.Succeeded)
        {
            return UserRegistrationResult.Success;
        }

        var errors = result.Errors.ToArray();
        var loginTaken = errors.Any(error =>
            string.Equals(error.Code, "DuplicateUserName", StringComparison.Ordinal));

        return UserRegistrationResult.Failure(
            loginTaken,
            errors.Select(error => error.Description).ToArray());
    }

    public async Task<string?> GetDisplayNameAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return (await userManager.FindByIdAsync(userId))?.DisplayName;
    }

    public async Task<bool> UpdateDisplayNameAsync(string userId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return false;
        }

        user.DisplayName = displayName;
        return (await userManager.UpdateAsync(user)).Succeeded;
    }

    private async Task RecordFailedAccessAsync(ApplicationUser user)
    {
        IdentityResult? result = await userManager.AccessFailedAsync(user);
        if (IsConcurrencyFailure(result))
        {
            result = await RetryIdentityUpdateAsync(
                user.Id,
                static (manager, currentUser) => manager.AccessFailedAsync(currentUser));
        }

        if (result is not null && !result.Succeeded)
        {
            LogIdentityUpdateFailure("record a failed sign-in", user.Id, result);
        }
    }

    private async Task<bool> ResetFailedAccessCountAsync(ApplicationUser user)
    {
        IdentityResult? result = await userManager.ResetAccessFailedCountAsync(user);
        if (IsConcurrencyFailure(result))
        {
            result = await RetryIdentityUpdateAsync(
                user.Id,
                static (manager, currentUser) => manager.ResetAccessFailedCountAsync(currentUser));
        }

        if (result is null)
        {
            return false;
        }

        if (result.Succeeded)
        {
            return true;
        }

        LogIdentityUpdateFailure("reset failed sign-ins", user.Id, result);
        return false;
    }

    private async Task<IdentityResult?> RetryIdentityUpdateAsync(
        string userId,
        Func<UserManager<ApplicationUser>, ApplicationUser, Task<IdentityResult>> update)
    {
        for (var retry = 0; retry < MaximumIdentityConcurrencyRetries; retry++)
        {
            // Identity keeps the stale user tracked after a conflict, so retries need a new scope.
            using var scope = scopeFactory.CreateScope();
            var retryUserManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var currentUser = await retryUserManager.FindByIdAsync(userId);
            if (currentUser is null || await retryUserManager.IsLockedOutAsync(currentUser))
            {
                return null;
            }

            var result = await update(retryUserManager, currentUser);
            if (!IsConcurrencyFailure(result))
            {
                return result;
            }
        }

        return IdentityResult.Failed(new IdentityError { Code = ConcurrencyFailureCode });
    }

    private static bool IsConcurrencyFailure(IdentityResult? result) =>
        result?.Errors.Any(error => string.Equals(error.Code, ConcurrencyFailureCode, StringComparison.Ordinal)) is true;

    private void LogIdentityUpdateFailure(string operation, string userId, IdentityResult result)
    {
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Identity could not {Operation} for user {UserId}: {ErrorCodes}.",
                operation,
                userId,
                string.Join(", ", result.Errors.Select(error => error.Code)));
        }
    }
}
