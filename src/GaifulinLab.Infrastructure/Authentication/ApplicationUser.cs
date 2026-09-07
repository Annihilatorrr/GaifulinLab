using Microsoft.AspNetCore.Identity;

namespace GaifulinLab.Infrastructure.Authentication;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "Author";
}
