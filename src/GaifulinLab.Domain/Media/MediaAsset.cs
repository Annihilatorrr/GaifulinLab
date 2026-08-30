using GaifulinLab.Domain.Common;

namespace GaifulinLab.Domain.Media;

public sealed class MediaAsset
{
    private MediaAsset()
    {
    }

    private MediaAsset(
        string originalFileName,
        string storedFileName,
        string relativePath,
        string contentType,
        long size,
        int? width,
        int? height,
        DateTimeOffset createdAt)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "File size must be greater than zero.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Image height must be greater than zero.");
        }

        Id = Guid.NewGuid();
        OriginalFileName = DomainRules.RequireTrimmed(originalFileName, nameof(originalFileName));
        StoredFileName = DomainRules.RequireTrimmed(storedFileName, nameof(storedFileName));
        RelativePath = DomainRules.RequireTrimmed(relativePath, nameof(relativePath));
        ContentType = DomainRules.RequireTrimmed(contentType, nameof(contentType));
        Size = size;
        Width = width;
        Height = height;
        CreatedAt = DomainRules.AsUtc(createdAt);
    }

    public Guid Id { get; private set; }

    public string OriginalFileName { get; private set; } = string.Empty;

    public string StoredFileName { get; private set; } = string.Empty;

    public string RelativePath { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long Size { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static MediaAsset Create(
        string originalFileName,
        string storedFileName,
        string relativePath,
        string contentType,
        long size,
        DateTimeOffset createdAt,
        int? width = null,
        int? height = null) =>
        new(originalFileName, storedFileName, relativePath, contentType, size, width, height, createdAt);
}
