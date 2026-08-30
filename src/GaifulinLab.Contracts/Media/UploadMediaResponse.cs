namespace GaifulinLab.Contracts.Media;

public sealed record UploadMediaResponse(
    Guid Id,
    string Url,
    string OriginalFileName,
    string ContentType,
    long Size,
    int? Width,
    int? Height);
