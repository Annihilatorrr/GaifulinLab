using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GaifulinLab.Infrastructure.Authentication;

public static class IdentityBootstrapExtensions
{
    public static async Task InitializeIdentityAsync(
        this IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(IdentityRoles.Admin))
        {
            EnsureSucceeded(
                await roleManager.CreateAsync(new IdentityRole(IdentityRoles.Admin)),
                $"create the '{IdentityRoles.Admin}' role");
        }

        var login = configuration["IDENTITY_BOOTSTRAP_ADMIN_LOGIN"]
            ?? configuration["Identity:BootstrapAdmin:Login"];
        var password = configuration["IDENTITY_BOOTSTRAP_ADMIN_PASSWORD"];
        var passwordHash = password is null
            ? configuration["IDENTITY_BOOTSTRAP_ADMIN_PASSWORD_HASH"]
                ?? configuration["Identity:BootstrapAdmin:PasswordHash"]
            : null;

        if (string.IsNullOrWhiteSpace(login)
            && string.IsNullOrWhiteSpace(password)
            && string.IsNullOrWhiteSpace(passwordHash))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(login)
            || (string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(passwordHash)))
        {
            throw new InvalidOperationException(
                "IDENTITY_BOOTSTRAP_ADMIN_LOGIN and either its password or password hash "
                + "must be configured together.");
        }

        var user = await userManager.FindByNameAsync(login);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = login,
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            var createResult = password is not null
                ? await userManager.CreateAsync(user, password)
                : await CreateWithPasswordHashAsync(userManager, user, passwordHash!);
            EnsureSucceeded(createResult, $"create bootstrap user '{login}'");
        }

        if (!await userManager.IsInRoleAsync(user, IdentityRoles.Admin))
        {
            EnsureSucceeded(
                await userManager.AddToRoleAsync(user, IdentityRoles.Admin),
                $"assign the '{IdentityRoles.Admin}' role to '{login}'");
        }
    }

    private static async Task<IdentityResult> CreateWithPasswordHashAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string passwordHash)
    {
        user.PasswordHash = passwordHash;
        return await userManager.CreateAsync(user);
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Could not {operation}: {string.Join("; ", result.Errors.Select(error => error.Description))}");
    }
}
