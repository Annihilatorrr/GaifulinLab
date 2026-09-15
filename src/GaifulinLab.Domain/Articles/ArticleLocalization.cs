using GaifulinLab.Domain.Common;

namespace GaifulinLab.Domain.Articles;

public sealed class ArticleLocalization
{
    private ArticleLocalization()
    {
    }

    internal ArticleLocalization(
        Guid articleId,
        string languageCode,
        string? title,
        string? summary,
        string? html,
        string? slug,
        DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        ArticleId = articleId;
        LanguageCode = DomainRules.NormalizeLanguageCode(languageCode);
        Status = PublicationStatus.Draft;
        UpdateContent(title, summary, html, slug, createdAt);
    }

    public Guid Id { get; private set; }

    public long Version { get; private set; }

    public Guid ArticleId { get; private set; }

    public Article Article { get; private set; } = null!;

    public string LanguageCode { get; private set; } = string.Empty;

    public string? Slug { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Summary { get; private set; }

    public string Html { get; private set; } = string.Empty;

    public Guid? CoverMediaAssetId { get; private set; }

    public string? SearchText { get; private set; }

    public int ReadingMinutes { get; private set; } = 1;

    public void SetCover(Guid? mediaAssetId) => CoverMediaAssetId = mediaAssetId;

    public void UpdateSearchText(string text, int readingMinutes)
    {
        SearchText = text;
        ReadingMinutes = Math.Max(1, readingMinutes);
    }

    public PublicationStatus Status { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset LastEditedAt { get; private set; }

    internal void UpdateContent(
        string? title,
        string? summary,
        string? html,
        string? slug,
        DateTimeOffset updatedAt)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        var normalizedSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        var normalizedHtml = html ?? string.Empty;
        var normalizedSlug = DomainRules.NormalizeOptionalSlug(slug);

        DomainRules.EnsureMaximumLength(normalizedTitle, ContentLimits.ArticleTitle, nameof(title));
        DomainRules.EnsureMaximumLength(normalizedSummary, ContentLimits.ArticleSummary, nameof(summary));
        DomainRules.EnsureMaximumLength(normalizedSlug, ContentLimits.ArticleSlug, nameof(slug));
        DomainRules.EnsureMaximumLength(normalizedHtml, ContentLimits.ArticleHtml, nameof(html));

        if (Status == PublicationStatus.Published)
        {
            EnsurePublishable(normalizedTitle, normalizedSlug, normalizedHtml);
        }

        Title = normalizedTitle;
        Summary = normalizedSummary;
        Html = normalizedHtml;
        Slug = normalizedSlug;
        var timestamp = DomainRules.AsUtc(updatedAt);
        UpdatedAt = timestamp;
        LastEditedAt = timestamp;
        Version++;
    }

    internal void Publish(DateTimeOffset publishedAt)
    {
        if (Status is not (PublicationStatus.Draft or PublicationStatus.Unpublished))
        {
            throw new InvalidOperationException("Only a draft or unpublished localization can be published.");
        }

        EnsurePublishable(Title, Slug, Html);

        var timestamp = DomainRules.AsUtc(publishedAt);
        Status = PublicationStatus.Published;
        if (PublishedAt is null)
        {
            PublishedAt = timestamp;
            LastEditedAt = timestamp;
        }

        UpdatedAt = timestamp;
    }

    internal void Unpublish(DateTimeOffset updatedAt)
    {
        if (Status != PublicationStatus.Published)
        {
            throw new InvalidOperationException("Only a published localization can be unpublished.");
        }

        Status = PublicationStatus.Unpublished;
        UpdatedAt = DomainRules.AsUtc(updatedAt);
    }

    private static void EnsurePublishable(string title, string? slug, string html)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("A title is required before publication.");
        }

        if (slug is null)
        {
            throw new InvalidOperationException("A slug is required before publication.");
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new InvalidOperationException("HTML content is required before publication.");
        }
    }
}
