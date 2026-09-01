using GaifulinLab.Domain.Articles;

namespace GaifulinLab.Domain.Pdf;

public sealed class PdfExportJob
{
    private PdfExportJob()
    {
    }

    private PdfExportJob(ArticleLocalization localization, decimal lineHeight, decimal blockSpacing, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        ArticleLocalizationId = localization.Id;
        LanguageCode = localization.LanguageCode;
        Slug = localization.Slug ?? throw new InvalidOperationException("A PDF export requires an article slug.");
        Title = localization.Title;
        Summary = localization.Summary;
        Markdown = localization.Markdown;
        PublishedAt = localization.PublishedAt;
        LineHeight = lineHeight;
        BlockSpacing = blockSpacing;
        Status = PdfExportStatus.Queued;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public Guid ArticleLocalizationId { get; private set; }

    public string LanguageCode { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string? Summary { get; private set; }

    public string Markdown { get; private set; } = string.Empty;

    public DateTimeOffset? PublishedAt { get; private set; }

    public decimal LineHeight { get; private set; }

    public decimal BlockSpacing { get; private set; }

    public PdfExportStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? RelativePath { get; private set; }

    public long? OutputSize { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PdfExportJob Create(
        ArticleLocalization localization,
        decimal lineHeight,
        decimal blockSpacing,
        DateTimeOffset createdAt) =>
        new(localization, lineHeight, blockSpacing, createdAt);

    public void Start(DateTimeOffset now, TimeSpan lease)
    {
        Status = PdfExportStatus.Processing;
        AttemptCount++;
        StartedAt = now.ToUniversalTime();
        LeaseExpiresAt = StartedAt.Value.Add(lease);
        ErrorMessage = null;
    }

    public void Complete(string relativePath, long outputSize, DateTimeOffset completedAt)
    {
        Status = PdfExportStatus.Completed;
        RelativePath = relativePath;
        OutputSize = outputSize;
        CompletedAt = completedAt.ToUniversalTime();
        LeaseExpiresAt = null;
        ErrorMessage = null;
    }

    public void Fail(string message)
    {
        Status = PdfExportStatus.Failed;
        LeaseExpiresAt = null;
        ErrorMessage = string.IsNullOrWhiteSpace(message)
            ? "The PDF export failed."
            : message[..Math.Min(message.Length, 1_000)];
    }
}
