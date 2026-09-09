namespace GaifulinLab.Domain.Pdf;

/// <summary>One successful reader download of a particular generated file in a calendar month.</summary>
public sealed class PdfDownloadUsage
{
    private PdfDownloadUsage()
    {
    }

    public PdfDownloadUsage(string userId, Guid pdfExportJobId, string relativePath, DateOnly period)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        PdfExportJobId = pdfExportJobId;
        RelativePath = relativePath;
        Period = period;
        DownloadedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid PdfExportJobId { get; private set; }
    public string RelativePath { get; private set; } = string.Empty;
    public DateOnly Period { get; private set; }
    public DateTimeOffset DownloadedAt { get; private set; }
}
