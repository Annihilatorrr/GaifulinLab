namespace GaifulinLab.Domain.Articles;

public sealed class ArticleView
{
    private const int VisitorHashLength = 64;

    private ArticleView()
    {
    }

    private ArticleView(Guid articleLocalizationId, string visitorHash, DateTimeOffset firstViewedAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(articleLocalizationId, Guid.Empty);

        ArticleLocalizationId = articleLocalizationId;
        VisitorHash = NormalizeVisitorHash(visitorHash);
        FirstViewedAt = firstViewedAt.ToUniversalTime();
    }

    public Guid ArticleLocalizationId { get; private set; }

    public string VisitorHash { get; private set; } = string.Empty;

    public DateTimeOffset FirstViewedAt { get; private set; }

    public static ArticleView Create(
        Guid articleLocalizationId,
        string visitorHash,
        DateTimeOffset firstViewedAt) =>
        new(articleLocalizationId, visitorHash, firstViewedAt);

    private static string NormalizeVisitorHash(string visitorHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visitorHash);

        var normalized = visitorHash.Trim().ToLowerInvariant();
        if (normalized.Length != VisitorHashLength
            || !normalized.All(character => Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "The visitor hash must be a 64-character hexadecimal SHA-256 value.",
                nameof(visitorHash));
        }

        return normalized;
    }
}
