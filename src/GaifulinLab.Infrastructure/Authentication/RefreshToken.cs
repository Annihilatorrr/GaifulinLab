namespace GaifulinLab.Infrastructure.Authentication;

/// <summary>
/// Persists the lifecycle of one browser refresh credential. The raw credential is never stored.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }

    /// <summary>
    /// Identifies the browser session across rotations so replay revokes only that session's active credentials.
    /// </summary>
    public Guid FamilyId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public ApplicationUser User { get; set; } = null!;
}
