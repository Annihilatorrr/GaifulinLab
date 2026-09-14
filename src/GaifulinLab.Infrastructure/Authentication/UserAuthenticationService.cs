using System.IdentityModel.Tokens.Jwt;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace GaifulinLab.Infrastructure.Authentication;

internal sealed class UserAuthenticationService(
    JwtAuthenticationSettings settings,
    UserManager<ApplicationUser> userManager,
    IServiceScopeFactory scopeFactory,
    ILogger<UserAuthenticationService> logger,
    AppDbContext? dbContext = null) : IUserAuthenticationService
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

        return await IssueSessionAsync(user, CancellationToken.None);
    }

    public async Task<IssuedAccessToken?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || dbContext is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var tokenHash = HashRefreshToken(refreshToken);
        var replacementRawToken = CreateRefreshToken();
        var replacementHash = HashRefreshToken(replacementRawToken);
        if (!dbContext.Database.IsRelational())
        {
            return await RotateRefreshTokenAsync(
                tokenHash,
                replacementRawToken,
                replacementHash,
                now,
                useTransaction: false,
                cancellationToken: cancellationToken);
        }

        // Npgsql retries transient failures. Each attempt must own its transaction so a retry cannot use an
        // externally started transaction or report a replacement that was never committed.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => RotateRefreshTokenAsync(
            tokenHash,
            replacementRawToken,
            replacementHash,
            now,
            useTransaction: true,
            cancellationToken: cancellationToken));
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || dbContext is null)
        {
            return;
        }

        var tokenHash = HashRefreshToken(refreshToken);
        if (dbContext.Database.IsRelational())
        {
            await dbContext.RefreshTokens
                .Where(candidate => candidate.TokenHash == tokenHash && candidate.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.RevokedAtUtc, DateTimeOffset.UtcNow), cancellationToken);
            return;
        }

        var token = await dbContext.RefreshTokens.SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);
        if (token is not null && token.RevokedAtUtc is null)
        {
            token.RevokedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<IssuedAccessToken> IssueSessionAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (dbContext is null)
        {
            throw new InvalidOperationException("Refresh-token persistence is not configured.");
        }

        var accessToken = await CreateAccessTokenAsync(user);
        var now = DateTimeOffset.UtcNow;
        var refreshToken = CreateRefreshToken();
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            FamilyId = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashRefreshToken(refreshToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(settings.RefreshTokenLifetime)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return accessToken with { RefreshToken = refreshToken };
    }

    private async Task<IssuedAccessToken> CreateAccessTokenAsync(ApplicationUser user)
    {
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

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt, string.Empty);
    }

    private async Task<IssuedAccessToken?> RotateRefreshTokenAsync(
        string tokenHash,
        string replacementRawToken,
        string replacementHash,
        DateTimeOffset now,
        bool useTransaction,
        CancellationToken cancellationToken)
    {
        await using var transaction = useTransaction
            ? await dbContext!.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var token = await dbContext!.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);
        if (token is null || token.ExpiresAtUtc <= now)
        {
            return null;
        }

        if (token.RevokedAtUtc is not null)
        {
            if (string.IsNullOrWhiteSpace(token.ReplacedByTokenHash))
            {
                return null;
            }

            // An execution-strategy retry after a successful commit sees its own replacement. Reuse that result;
            // a different replacement means the submitted token was replayed and its browser session is compromised.
            if (string.Equals(token.ReplacedByTokenHash, replacementHash, StringComparison.Ordinal))
            {
                return await ReturnCommittedReplacementAsync(token, replacementRawToken, now, transaction, cancellationToken);
            }

            await RevokeSessionFamilyAsync(token.FamilyId, now, cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return null;
        }

        var user = await userManager.FindByIdAsync(token.UserId);
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        // A conditional update is the database concurrency boundary: only one request can consume this token.
        var consumed = useTransaction
            ? await dbContext.RefreshTokens
                .Where(candidate => candidate.Id == token.Id
                    && candidate.RevokedAtUtc == null
                    && candidate.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.RevokedAtUtc, now)
                    .SetProperty(candidate => candidate.ReplacedByTokenHash, replacementHash), cancellationToken) == 1
            : await ConsumeInMemoryAsync(token.Id, now, replacementHash, cancellationToken);
        if (!consumed)
        {
            var current = await dbContext.RefreshTokens
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == token.Id, cancellationToken);
            if (current?.RevokedAtUtc is not null && !string.IsNullOrWhiteSpace(current.ReplacedByTokenHash))
            {
                if (string.Equals(current.ReplacedByTokenHash, replacementHash, StringComparison.Ordinal))
                {
                    return await ReturnCommittedReplacementAsync(current, replacementRawToken, now, transaction, cancellationToken);
                }

                await RevokeSessionFamilyAsync(current.FamilyId, now, cancellationToken);
                await CommitAsync(transaction, cancellationToken);
            }

            return null;
        }

        var accessToken = await CreateAccessTokenAsync(user);
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            FamilyId = token.FamilyId,
            UserId = user.Id,
            TokenHash = replacementHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(settings.RefreshTokenLifetime)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return accessToken with { RefreshToken = replacementRawToken };
    }

    private async Task<IssuedAccessToken?> ReturnCommittedReplacementAsync(
        RefreshToken consumedToken,
        string replacementRawToken,
        DateTimeOffset now,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var replacement = await dbContext!.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == consumedToken.ReplacedByTokenHash
                && candidate.UserId == consumedToken.UserId
                && candidate.RevokedAtUtc == null
                && candidate.ExpiresAtUtc > now, cancellationToken);
        if (replacement is null)
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(consumedToken.UserId);
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        var accessToken = await CreateAccessTokenAsync(user);
        await CommitAsync(transaction, cancellationToken);
        return accessToken with { RefreshToken = replacementRawToken };
    }

    private async Task RevokeSessionFamilyAsync(
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (dbContext!.Database.IsRelational())
        {
            await dbContext.RefreshTokens
                .Where(candidate => candidate.FamilyId == familyId && candidate.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.RevokedAtUtc, now), cancellationToken);
            return;
        }

        var activeTokens = await dbContext.RefreshTokens
            .Where(candidate => candidate.FamilyId == familyId && candidate.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var activeToken in activeTokens)
        {
            activeToken.RevokedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Task CommitAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

    private async Task<bool> ConsumeInMemoryAsync(
        Guid tokenId,
        DateTimeOffset now,
        string replacementHash,
        CancellationToken cancellationToken)
    {
        var token = await dbContext!.RefreshTokens.SingleOrDefaultAsync(candidate => candidate.Id == tokenId, cancellationToken);
        if (token is null || token.RevokedAtUtc is not null || token.ExpiresAtUtc <= now)
        {
            return false;
        }

        token.RevokedAtUtc = now;
        token.ReplacedByTokenHash = replacementHash;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string CreateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

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

    public async Task<UpdateDisplayNameResult> UpdateDisplayNameAsync(string userId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UpdateDisplayNameResult.NotFound;
        }

        user.DisplayName = displayName;
        var result = await userManager.UpdateAsync(user);
        if (result.Succeeded)
        {
            return UpdateDisplayNameResult.Updated;
        }

        if (IsConcurrencyFailure(result))
        {
            return UpdateDisplayNameResult.Conflict;
        }

        throw new InvalidOperationException(
            $"Identity could not update the display name for user '{userId}': "
            + string.Join(", ", result.Errors.Select(error => error.Code)));
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
