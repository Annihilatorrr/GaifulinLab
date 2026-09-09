using Microsoft.AspNetCore.Identity;

namespace GaifulinLab.Infrastructure.Authentication;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "Author";

    /// <summary>Free is the safe default until a billing integration changes it.</summary>
    public PdfSubscriptionTier PdfSubscriptionTier { get; set; } = PdfSubscriptionTier.Free;
}
