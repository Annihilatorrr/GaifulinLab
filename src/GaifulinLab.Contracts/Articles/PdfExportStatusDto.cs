namespace GaifulinLab.Contracts.Articles;

public sealed record PdfExportStatusDto(
    Guid Id,
    string Status,
    string? ErrorMessage,
    string? DownloadUrl);
