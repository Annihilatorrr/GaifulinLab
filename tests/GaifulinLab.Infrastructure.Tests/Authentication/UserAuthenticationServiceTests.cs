using GaifulinLab.Infrastructure.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaifulinLab.Infrastructure.Tests.Authentication;

public sealed class UserAuthenticationServiceTests
{
    [Fact]
    public async Task AuthenticateAsync_WhenFailedAccessUpdateConflicts_RetriesWithFreshUserState()
    {
        // Arrange: the first update simulates Identity's optimistic-concurrency failure.
        var state = new UserStoreState(CreateUser());
        using var serviceProvider = CreateServiceProvider(state);
        using var scope = serviceProvider.CreateScope();
        var service = new UserAuthenticationService(
            new JwtAuthenticationSettings(
                "GaifulinLab.Tests",
                "GaifulinLab.Tests.Client",
                "test-signing-key-that-is-at-least-32-bytes-long",
                TimeSpan.FromMinutes(5)),
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            scope.ServiceProvider.GetRequiredService<ILogger<UserAuthenticationService>>());

        // Act: an invalid password must be recorded even when the initial update conflicts.
        var token = await service.AuthenticateAsync("admin", "wrong-password");

        // Assert: a fresh manager retries the increment instead of losing this failed attempt.
        Assert.Null(token);
        Assert.Equal(1, state.Current.AccessFailedCount);
        Assert.Equal(2, state.UpdateAttempts);
    }

    private static ServiceProvider CreateServiceProvider(UserStoreState state)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddIdentityCore<ApplicationUser>();
        services.AddScoped<IUserStore<ApplicationUser>, ConflictOnceUserStore>();
        return services.BuildServiceProvider();
    }

    private static ApplicationUser CreateUser()
    {
        var user = new ApplicationUser
        {
            Id = "admin-id",
            UserName = "admin",
            NormalizedUserName = "ADMIN",
            DisplayName = "Administrator",
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(
            user,
            "Correct-horse-battery-staple-1!");
        return user;
    }

    private sealed class ConflictOnceUserStore(UserStoreState state)
        : IUserPasswordStore<ApplicationUser>, IUserLockoutStore<ApplicationUser>
    {
        public void Dispose()
        {
        }

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Failed());

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Failed());

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationUser?>(
                string.Equals(state.Current.Id, userId, StringComparison.Ordinal)
                    ? Clone(state.Current)
                    : null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationUser?>(
                string.Equals(state.Current.NormalizedUserName, normalizedUserName, StringComparison.Ordinal)
                    ? Clone(state.Current)
                    : null);

        public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.AccessFailedCount);

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id);

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PasswordHash);

        public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.LockoutEnabled);

        public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.LockoutEnd);

        public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            user.AccessFailedCount++;
            return Task.FromResult(user.AccessFailedCount);
        }

        public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PasswordHash is not null);

        public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            user.AccessFailedCount = 0;
            return Task.CompletedTask;
        }

        public Task SetLockoutEnabledAsync(
            ApplicationUser user,
            bool enabled,
            CancellationToken cancellationToken)
        {
            user.LockoutEnabled = enabled;
            return Task.CompletedTask;
        }

        public Task SetLockoutEndDateAsync(
            ApplicationUser user,
            DateTimeOffset? lockoutEnd,
            CancellationToken cancellationToken)
        {
            user.LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }

        public Task SetNormalizedUserNameAsync(
            ApplicationUser user,
            string? normalizedName,
            CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetPasswordHashAsync(
            ApplicationUser user,
            string? passwordHash,
            CancellationToken cancellationToken)
        {
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task SetUserNameAsync(
            ApplicationUser user,
            string? userName,
            CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            state.UpdateAttempts++;
            if (state.UpdateAttempts == 1)
            {
                return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure" }));
            }

            state.Current = Clone(user);
            return Task.FromResult(IdentityResult.Success);
        }

        private static ApplicationUser Clone(ApplicationUser user) =>
            new()
            {
                Id = user.Id,
                UserName = user.UserName,
                NormalizedUserName = user.NormalizedUserName,
                PasswordHash = user.PasswordHash,
                SecurityStamp = user.SecurityStamp,
                ConcurrencyStamp = user.ConcurrencyStamp,
                LockoutEnd = user.LockoutEnd,
                LockoutEnabled = user.LockoutEnabled,
                AccessFailedCount = user.AccessFailedCount,
                DisplayName = user.DisplayName
            };
    }

    private sealed class UserStoreState(ApplicationUser current)
    {
        public ApplicationUser Current { get; set; } = current;

        public int UpdateAttempts { get; set; }
    }
}
