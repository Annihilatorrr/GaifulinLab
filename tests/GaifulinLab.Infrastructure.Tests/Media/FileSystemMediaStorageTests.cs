using GaifulinLab.Infrastructure.Media;

namespace GaifulinLab.Infrastructure.Tests.Media;

public sealed class FileSystemMediaStorageTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        $"gaifulinlab-media-storage-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveOpenAndDelete_RoundTripsContent()
    {
        var storage = new FileSystemMediaStorage(_rootPath);
        var expected = "image bytes"u8.ToArray();

        await storage.SaveAsync("2026/08/image.png", new MemoryStream(expected), CancellationToken.None);
        await using (var stored = await storage.OpenReadAsync(
                         "2026/08/image.png",
                         CancellationToken.None))
        {
            Assert.NotNull(stored);
            using var copy = new MemoryStream();
            await stored.CopyToAsync(copy);
            Assert.Equal(expected, copy.ToArray());
        }

        await storage.DeleteAsync("2026/08/image.png", CancellationToken.None);
        Assert.Null(await storage.OpenReadAsync("2026/08/image.png", CancellationToken.None));
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("../../outside.png")]
    public async Task Save_WithEscapingPath_IsRejected(string relativePath)
    {
        var storage = new FileSystemMediaStorage(_rootPath);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(
            relativePath,
            new MemoryStream("content"u8.ToArray()),
            CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
