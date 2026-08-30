using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Media;
using GaifulinLab.Domain.Media;
using MediatR;

namespace GaifulinLab.Application.Media.UploadMedia;

internal sealed class UploadMediaCommandHandler(
    IAppDbContext dbContext,
    IMediaStorage storage,
    TimeProvider timeProvider) : IRequestHandler<UploadMediaCommand, UploadMediaResponse>
{
    public async Task<UploadMediaResponse> Handle(
        UploadMediaCommand request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var originalFileName = NormalizeOriginalFileName(request.OriginalFileName);
        var format = await DetectFormat(request.Content, cancellationToken);
        ValidateDeclaredFormat(originalFileName, request.ContentType, format);

        var now = timeProvider.GetUtcNow();
        var storedFileName = $"{Guid.NewGuid():N}{format.Extension}";
        var relativePath = $"{now:yyyy/MM}/{storedFileName}";
        var asset = MediaAsset.Create(
            originalFileName,
            storedFileName,
            relativePath,
            format.ContentType,
            request.Size,
            now);

        await storage.SaveAsync(relativePath, request.Content, cancellationToken);
        try
        {
            dbContext.MediaAssets.Add(asset);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(relativePath, CancellationToken.None);
            throw;
        }

        return new UploadMediaResponse(
            asset.Id,
            $"/media/{asset.Id}",
            asset.OriginalFileName,
            asset.ContentType,
            asset.Size,
            asset.Width,
            asset.Height);
    }

    private static void ValidateRequest(UploadMediaCommand request)
    {
        ArgumentNullException.ThrowIfNull(request.Content);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OriginalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContentType);

        if (request.Size <= 0)
        {
            throw new ArgumentException("The image file is empty.", nameof(request));
        }

        if (request.Size > MediaUploadLimits.MaximumFileSize)
        {
            throw new ArgumentException(
                $"The image file cannot exceed {MediaUploadLimits.MaximumFileSize / 1024 / 1024} MB.",
                nameof(request));
        }

        if (!request.Content.CanSeek)
        {
            throw new ArgumentException("The image stream must support seeking.", nameof(request));
        }
    }

    private static string NormalizeOriginalFileName(string suppliedFileName)
    {
        var normalizedSeparators = suppliedFileName.Replace('\\', '/');
        var fileName = normalizedSeparators[(normalizedSeparators.LastIndexOf('/') + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 || fileName.Any(char.IsControl))
        {
            throw new ArgumentException("The original file name is invalid.", nameof(suppliedFileName));
        }

        return fileName;
    }

    private static async Task<MediaFormat> DetectFormat(
        Stream content,
        CancellationToken cancellationToken)
    {
        var originalPosition = content.Position;
        var header = new byte[12];
        var bytesRead = 0;
        while (bytesRead < header.Length)
        {
            var read = await content.ReadAsync(
                header.AsMemory(bytesRead, header.Length - bytesRead),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            bytesRead += read;
        }

        content.Position = originalPosition;

        if (bytesRead >= 8 && header.AsSpan(0, 8).SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
        {
            return new MediaFormat(".png", "image/png");
        }

        if (bytesRead >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff)
        {
            return new MediaFormat(".jpg", "image/jpeg", ".jpeg");
        }

        if (bytesRead >= 6
            && (header.AsSpan(0, 6).SequenceEqual("GIF87a"u8)
                || header.AsSpan(0, 6).SequenceEqual("GIF89a"u8)))
        {
            return new MediaFormat(".gif", "image/gif");
        }

        if (bytesRead >= 12
            && header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && header.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return new MediaFormat(".webp", "image/webp");
        }

        throw new ArgumentException(
            "Only PNG, JPEG, GIF and WebP images are supported.",
            nameof(content));
    }

    private static void ValidateDeclaredFormat(
        string fileName,
        string declaredContentType,
        MediaFormat detectedFormat)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var contentType = declaredContentType.Trim().ToLowerInvariant();

        if (!detectedFormat.Extensions.Contains(extension, StringComparer.Ordinal)
            || !string.Equals(contentType, detectedFormat.ContentType, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The file extension, content type and image contents do not match.",
                nameof(fileName));
        }
    }

    private sealed record MediaFormat(
        string Extension,
        string ContentType,
        params string[] AlternativeExtensions)
    {
        public IReadOnlyList<string> Extensions { get; } = [Extension, .. AlternativeExtensions];
    }
}
